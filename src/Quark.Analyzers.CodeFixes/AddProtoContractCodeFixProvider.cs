using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quark.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for adding missing ProtoBuf serialization attributes.
/// Adds [ProtoContract] to types and [ProtoMember(n)] to properties.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddProtoContractCodeFixProvider)), Shared]
public class AddProtoContractCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            MissingProtoContractAnalyzer.MissingProtoContractDiagnosticId,
            MissingProtoContractAnalyzer.MissingProtoMemberDiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        if (diagnostic.Id == MissingProtoContractAnalyzer.MissingProtoContractDiagnosticId)
        {
            // Need to find the type declaration that corresponds to this diagnostic
            // The diagnostic is on a type reference (parameter or return type)
            // We need to find the actual type declaration
            var typeIdentifier = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                .OfType<IdentifierNameSyntax>()
                .FirstOrDefault();

            if (typeIdentifier != null)
            {
                var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
                if (semanticModel == null)
                    return;

                var typeInfo = semanticModel.GetTypeInfo(typeIdentifier, context.CancellationToken);
                if (typeInfo.Type != null)
                {
                    // Find the declaration of this type
                    var typeDeclarationLocation = typeInfo.Type.Locations.FirstOrDefault(l => l.IsInSource);
                    if (typeDeclarationLocation != null)
                    {
                        var typeDeclarationTree = typeDeclarationLocation.SourceTree;
                        if (typeDeclarationTree != null)
                        {
                            // Register code fix to add [ProtoContract] and [ProtoMember] attributes
                            context.RegisterCodeFix(
                                CodeAction.Create(
                                    title: "Add [ProtoContract] and [ProtoMember] attributes",
                                    createChangedSolution: c => AddProtoAttributesAsync(
                                        context.Document.Project.Solution,
                                        typeInfo.Type,
                                        c),
                                    equivalenceKey: "AddProtoAttributes"),
                                diagnostic);
                        }
                    }
                }
            }
        }
        else if (diagnostic.Id == MissingProtoContractAnalyzer.MissingProtoMemberDiagnosticId)
        {
            // Find the property declaration
            var propertyDeclaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault();

            // Also check for record parameters
            var recordParameter = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                .OfType<ParameterSyntax>()
                .FirstOrDefault();

            if (propertyDeclaration != null || recordParameter != null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Add [ProtoMember] attribute",
                        createChangedDocument: c => AddProtoMemberAttributeAsync(
                            context.Document,
                            propertyDeclaration,
                            recordParameter,
                            c),
                        equivalenceKey: "AddProtoMember"),
                    diagnostic);
            }
        }
    }

    private static async Task<Solution> AddProtoAttributesAsync(
        Solution solution,
        ITypeSymbol typeSymbol,
        CancellationToken cancellationToken)
    {
        var typeLocation = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (typeLocation == null)
            return solution;

        var document = solution.GetDocument(typeLocation.SourceTree);
        if (document == null)
            return solution;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return solution;

        var typeDeclaration = root.FindToken(typeLocation.SourceSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (typeDeclaration == null)
            return solution;

        // Add [ProtoContract] to the type
        var protoContractAttribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("ProtoContract"));

        var protoContractList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(protoContractAttribute))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        var newTypeDeclaration = typeDeclaration.AddAttributeLists(protoContractList);

        // Add [ProtoMember(n)] to all properties/parameters
        if (typeDeclaration is RecordDeclarationSyntax recordDecl && recordDecl.ParameterList != null)
        {
            // Handle record primary constructor parameters
            var newParameters = new List<ParameterSyntax>();
            int memberNumber = 1;

            foreach (var param in recordDecl.ParameterList.Parameters)
            {
                var protoMemberAttribute = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("ProtoMember"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(memberNumber++))))));

                var protoMemberList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(protoMemberAttribute));

                var newParam = param.AddAttributeLists(protoMemberList);
                newParameters.Add(newParam);
            }

            var newParameterList = SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList(newParameters));

            newTypeDeclaration = ((RecordDeclarationSyntax)newTypeDeclaration)
                .WithParameterList(newParameterList);
        }
        else
        {
            // Handle regular properties
            var properties = typeDeclaration.Members.OfType<PropertyDeclarationSyntax>().ToList();
            int memberNumber = 1;

            foreach (var property in properties)
            {
                var protoMemberAttribute = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("ProtoMember"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(memberNumber++))))));

                var protoMemberList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(protoMemberAttribute))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                var newProperty = property.AddAttributeLists(protoMemberList);
                newTypeDeclaration = newTypeDeclaration.ReplaceNode(property, newProperty);
            }
        }

        var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);

        // Add using directive if needed
        var compilationUnit = newRoot as CompilationUnitSyntax;
        if (compilationUnit != null)
        {
            var hasProtoBufUsing = compilationUnit.Usings
                .Any(u => u.Name?.ToFullString().Trim() == "ProtoBuf");

            if (!hasProtoBufUsing)
            {
                var usingDirective = SyntaxFactory.UsingDirective(
                    SyntaxFactory.IdentifierName("ProtoBuf"))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                newRoot = compilationUnit.WithUsings(
                    compilationUnit.Usings.Add(usingDirective));
            }
        }

        var newDocument = document.WithSyntaxRoot(newRoot);
        return newDocument.Project.Solution;
    }

    private static async Task<Document> AddProtoMemberAttributeAsync(
        Document document,
        PropertyDeclarationSyntax? propertyDeclaration,
        ParameterSyntax? recordParameter,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        // Determine the next available ProtoMember number
        int nextMemberNumber = 1;

        if (propertyDeclaration != null)
        {
            var containingType = propertyDeclaration.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (containingType != null)
            {
                nextMemberNumber = GetNextProtoMemberNumber(containingType, semanticModel);

                var protoMemberAttribute = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("ProtoMember"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(nextMemberNumber))))));

                var protoMemberList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(protoMemberAttribute))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                var newProperty = propertyDeclaration.AddAttributeLists(protoMemberList);
                var newRoot = root.ReplaceNode(propertyDeclaration, newProperty);

                return document.WithSyntaxRoot(newRoot);
            }
        }
        else if (recordParameter != null)
        {
            var recordDecl = recordParameter.Ancestors().OfType<RecordDeclarationSyntax>().FirstOrDefault();
            if (recordDecl != null)
            {
                nextMemberNumber = GetNextProtoMemberNumber(recordDecl, semanticModel);

                var protoMemberAttribute = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("ProtoMember"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(nextMemberNumber))))));

                var protoMemberList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(protoMemberAttribute));

                var newParameter = recordParameter.AddAttributeLists(protoMemberList);
                var newRoot = root.ReplaceNode(recordParameter, newParameter);

                return document.WithSyntaxRoot(newRoot);
            }
        }

        return document;
    }

    private static int GetNextProtoMemberNumber(TypeDeclarationSyntax typeDecl, SemanticModel semanticModel)
    {
        var maxNumber = 0;

        // Check properties
        foreach (var property in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            var propertySymbol = semanticModel.GetDeclaredSymbol(property);
            if (propertySymbol != null)
            {
                var protoMemberAttr = propertySymbol.GetAttributes()
                    .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == "ProtoBuf.ProtoMemberAttribute");

                if (protoMemberAttr != null && protoMemberAttr.ConstructorArguments.Length > 0)
                {
                    if (protoMemberAttr.ConstructorArguments[0].Value is int memberNumber)
                    {
                        maxNumber = Math.Max(maxNumber, memberNumber);
                    }
                }
            }
        }

        // Check record parameters
        if (typeDecl is RecordDeclarationSyntax recordDecl && recordDecl.ParameterList != null)
        {
            foreach (var param in recordDecl.ParameterList.Parameters)
            {
                // Get the property symbol for this parameter
                var typeSymbol = semanticModel.GetDeclaredSymbol(recordDecl);
                if (typeSymbol != null)
                {
                    var propertySymbol = typeSymbol.GetMembers(param.Identifier.Text)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault();

                    if (propertySymbol != null)
                    {
                        var protoMemberAttr = propertySymbol.GetAttributes()
                            .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == "ProtoBuf.ProtoMemberAttribute");

                        if (protoMemberAttr != null && protoMemberAttr.ConstructorArguments.Length > 0)
                        {
                            if (protoMemberAttr.ConstructorArguments[0].Value is int memberNumber)
                            {
                                maxNumber = Math.Max(maxNumber, memberNumber);
                            }
                        }
                    }
                }
            }
        }

        return maxNumber + 1;
    }
}
