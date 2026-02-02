using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quark.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for scaffolding supervision hierarchy implementations.
/// Generates ISupervisor implementation with common supervision patterns.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SupervisionScaffoldCodeFixProvider)), Shared]
public class SupervisionScaffoldCodeFixProvider : CodeFixProvider
{
    public const string DiagnosticId = "QUARK011";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Scaffold supervision hierarchy",
        "Class '{0}' can implement ISupervisor",
        "Quark.Supervision",
        DiagnosticSeverity.Hidden, // Hidden - only shows as a code action
        isEnabledByDefault: true,
        description: "Generate ISupervisor implementation with common supervision patterns.");

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.FirstOrDefault();
        if (diagnostic == null)
            return;

        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var classDeclaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<ClassDeclarationSyntax>().FirstOrDefault();

        if (classDeclaration == null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol == null)
            return;

        // Only offer this for actor classes
        if (!IsActorClass(classSymbol))
            return;

        // Check if already implements ISupervisor
        if (ImplementsISupervisor(classSymbol))
            return;

        // Register code fix for restart strategy
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Implement ISupervisor (restart on failure)",
                createChangedDocument: c => ImplementSupervisorAsync(context.Document, classDeclaration, "Restart", c),
                equivalenceKey: "ImplementSupervisorRestart"),
            diagnostic);

        // Register code fix for stop strategy
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Implement ISupervisor (stop on failure)",
                createChangedDocument: c => ImplementSupervisorAsync(context.Document, classDeclaration, "Stop", c),
                equivalenceKey: "ImplementSupervisorStop"),
            diagnostic);

        // Register code fix for custom strategy
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Implement ISupervisor (custom strategy)",
                createChangedDocument: c => ImplementSupervisorAsync(context.Document, classDeclaration, "Custom", c),
                equivalenceKey: "ImplementSupervisorCustom"),
            diagnostic);
    }

    private static bool IsActorClass(INamedTypeSymbol classSymbol)
    {
        var hasActorAttribute = classSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == "Quark.Abstractions.ActorAttribute");

        if (hasActorAttribute)
            return true;

        var baseType = classSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.Name == "ActorBase" || baseType.Name == "StatefulActorBase")
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    private static bool ImplementsISupervisor(INamedTypeSymbol classSymbol)
    {
        return classSymbol.AllInterfaces.Any(i => i.Name == "ISupervisor");
    }

    private static async Task<Document> ImplementSupervisorAsync(
        Document document,
        ClassDeclarationSyntax classDeclaration,
        string strategy,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Add ISupervisor to base list
        var supervisorInterface = SyntaxFactory.SimpleBaseType(
            SyntaxFactory.ParseTypeName("ISupervisor"));

        var newClass = classDeclaration;
        if (classDeclaration.BaseList == null)
        {
            newClass = classDeclaration.WithBaseList(
                SyntaxFactory.BaseList(
                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(supervisorInterface)));
        }
        else
        {
            newClass = classDeclaration.WithBaseList(
                classDeclaration.BaseList.AddTypes(supervisorInterface));
        }

        // Generate OnChildFailureAsync method based on strategy
        var method = GenerateOnChildFailureMethod(strategy);
        newClass = newClass.AddMembers(method);

        // Add using directives
        var compilationUnit = root as CompilationUnitSyntax;
        if (compilationUnit != null)
        {
            var usingsToAdd = new[]
            {
                "Quark.Abstractions",
                "Quark.Abstractions.Supervision",
                "System.Threading.Tasks"
            };

            foreach (var usingNamespace in usingsToAdd)
            {
                var hasUsing = compilationUnit.Usings
                    .Any(u => u.Name?.ToString() == usingNamespace);

                if (!hasUsing)
                {
                    var usingDirective = SyntaxFactory.UsingDirective(
                        SyntaxFactory.ParseName(usingNamespace));
                    compilationUnit = compilationUnit.AddUsings(usingDirective);
                }
            }

            var newRoot = compilationUnit.ReplaceNode(classDeclaration, newClass);
            return document.WithSyntaxRoot(newRoot);
        }

        var simpleRoot = root.ReplaceNode(classDeclaration, newClass);
        return document.WithSyntaxRoot(simpleRoot);
    }

    private static MethodDeclarationSyntax GenerateOnChildFailureMethod(string strategy)
    {
        var methodBody = strategy switch
        {
            "Restart" => @"
        // Log the failure
        // TODO: Add logging

        // Restart the child actor
        return Task.FromResult(SupervisionDirective.Restart);",
            "Stop" => @"
        // Log the failure
        // TODO: Add logging

        // Stop the failed child actor
        return Task.FromResult(SupervisionDirective.Stop);",
            _ => @"
        // Custom supervision logic based on exception type
        return context.Exception switch
        {
            TimeoutException => Task.FromResult(SupervisionDirective.Resume),
            InvalidOperationException => Task.FromResult(SupervisionDirective.Restart),
            OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
            _ => Task.FromResult(SupervisionDirective.Escalate)
        };"
        };

        var method = SyntaxFactory.ParseMemberDeclaration($@"
    /// <summary>
    /// Handles child actor failures.
    /// </summary>
    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {{{methodBody}
    }}") as MethodDeclarationSyntax;

        return method ?? throw new InvalidOperationException("Failed to parse method declaration");
    }
}
