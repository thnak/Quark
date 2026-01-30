using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quark.Analyzers;

/// <summary>
/// Code fix provider for adding missing [Actor] attribute to actor classes.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingActorAttributeCodeFixProvider)), Shared]
public class MissingActorAttributeCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingActorAttributeAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var declaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<ClassDeclarationSyntax>().FirstOrDefault();

        if (declaration == null)
            return;

        // Register code fix to add [Actor] attribute
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add [Actor] attribute",
                createChangedDocument: c => AddActorAttributeAsync(context.Document, declaration, c),
                equivalenceKey: "AddActorAttribute"),
            diagnostic);
    }

    private static async Task<Document> AddActorAttributeAsync(
        Document document,
        ClassDeclarationSyntax classDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Create [Actor] attribute
        var actorAttribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("Actor"));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(actorAttribute))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        // Add the attribute to the class
        var newClass = classDeclaration.AddAttributeLists(attributeList);

        // If the class doesn't have a using for Quark.Abstractions, we should add it
        var newRoot = root.ReplaceNode(classDeclaration, newClass);
        
        // Check if we need to add using directive
        var compilationUnit = newRoot as CompilationUnitSyntax;
        if (compilationUnit != null)
        {
            var hasQuarkUsing = compilationUnit.Usings
                .Any(u => u.Name?.ToFullString().Trim() == "Quark.Abstractions");

            if (!hasQuarkUsing)
            {
                var usingDirective = SyntaxFactory.UsingDirective(
                    SyntaxFactory.IdentifierName("Quark.Abstractions"))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                // Insert at the beginning to maintain alphabetical order
                newRoot = compilationUnit.WithUsings(
                    compilationUnit.Usings.Insert(0, usingDirective));
            }
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
