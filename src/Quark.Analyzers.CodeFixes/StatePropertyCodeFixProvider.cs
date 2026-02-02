using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quark.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for generating state properties with [QuarkState] attribute.
/// This provides a quick way to add state management to actor classes.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StatePropertyCodeFixProvider)), Shared]
public class StatePropertyCodeFixProvider : CodeFixProvider
{
    public const string DiagnosticId = "QUARK010";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Generate QuarkState property",
        "Class '{0}' can have state properties generated",
        "Quark.StateManagement",
        DiagnosticSeverity.Hidden, // Hidden - only shows as a code action, not a warning
        isEnabledByDefault: true,
        description: "Generate a property with [QuarkState] attribute for actor state persistence.");

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

        // Register code fix to add a simple state property
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add QuarkState property (string)",
                createChangedDocument: c => AddStatePropertyAsync(context.Document, classDeclaration, "string", "State", c),
                equivalenceKey: "AddStringStateProperty"),
            diagnostic);

        // Register code fix to add an int state property
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add QuarkState property (int)",
                createChangedDocument: c => AddStatePropertyAsync(context.Document, classDeclaration, "int", "Counter", c),
                equivalenceKey: "AddIntStateProperty"),
            diagnostic);

        // Register code fix to add a custom state object property
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add QuarkState property (custom type)",
                createChangedDocument: c => AddStatePropertyAsync(context.Document, classDeclaration, $"{classSymbol.Name}State", "State", c),
                equivalenceKey: "AddCustomStateProperty"),
            diagnostic);
    }

    private static bool IsActorClass(INamedTypeSymbol classSymbol)
    {
        // Check for [Actor] attribute
        var hasActorAttribute = classSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == "Quark.Abstractions.ActorAttribute");

        if (hasActorAttribute)
            return true;

        // Check if it inherits from ActorBase or StatefulActorBase
        var baseType = classSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.Name == "ActorBase" || baseType.Name == "StatefulActorBase")
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    private static async Task<Document> AddStatePropertyAsync(
        Document document,
        ClassDeclarationSyntax classDeclaration,
        string propertyType,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Create the property with [QuarkState] attribute
        var property = SyntaxFactory.PropertyDeclaration(
                SyntaxFactory.ParseTypeName(propertyType),
                SyntaxFactory.Identifier(propertyName))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
            .AddAttributeLists(
                SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Attribute(
                            SyntaxFactory.IdentifierName("QuarkState")))))
            .WithLeadingTrivia(
                SyntaxFactory.Trivia(
                    SyntaxFactory.DocumentationCommentTrivia(
                        SyntaxKind.SingleLineDocumentationCommentTrivia,
                        SyntaxFactory.List(new XmlNodeSyntax[]
                        {
                            SyntaxFactory.XmlText("/// "),
                            SyntaxFactory.XmlElement(
                                SyntaxFactory.XmlElementStartTag(SyntaxFactory.XmlName("summary")),
                                SyntaxFactory.XmlElementEndTag(SyntaxFactory.XmlName("summary")))
                                .WithContent(
                                    SyntaxFactory.SingletonList<XmlNodeSyntax>(
                                        SyntaxFactory.XmlText($"Persisted state for this actor."))),
                            SyntaxFactory.XmlText(
                                SyntaxFactory.XmlTextNewLine("\n", false))
                        }))))
            .NormalizeWhitespace();

        // Add the property to the class
        var newClass = classDeclaration.AddMembers(property);

        // Check if we need to add using directives
        var compilationUnit = root as CompilationUnitSyntax;
        if (compilationUnit != null)
        {
            var hasQuarkAbstractionsUsing = compilationUnit.Usings
                .Any(u => u.Name?.ToString() == "Quark.Abstractions");

            if (!hasQuarkAbstractionsUsing)
            {
                var usingDirective = SyntaxFactory.UsingDirective(
                    SyntaxFactory.ParseName("Quark.Abstractions"));
                
                compilationUnit = compilationUnit.AddUsings(usingDirective);
            }

            var newRoot = compilationUnit.ReplaceNode(classDeclaration, newClass);
            return document.WithSyntaxRoot(newRoot);
        }

        var simpleRoot = root.ReplaceNode(classDeclaration, newClass);
        return document.WithSyntaxRoot(simpleRoot);
    }
}
