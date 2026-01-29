using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer for validating QuarkStream attribute usage.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class QuarkStreamAnalyzer : DiagnosticAnalyzer
{
    public const string InvalidNamespaceDiagnosticId = "QUARK001";
    public const string MissingInterfaceDiagnosticId = "QUARK002";
    public const string DuplicateStreamDiagnosticId = "QUARK003";

    private static readonly DiagnosticDescriptor InvalidNamespaceRule = new DiagnosticDescriptor(
        InvalidNamespaceDiagnosticId,
        "Invalid stream namespace format",
        "Stream namespace '{0}' should follow the format 'category/subcategory' (e.g., 'orders/processed')",
        "Quark.Streams",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Stream namespaces should follow a hierarchical naming convention.");

    private static readonly DiagnosticDescriptor MissingInterfaceRule = new DiagnosticDescriptor(
        MissingInterfaceDiagnosticId,
        "Missing IStreamConsumer interface",
        "Actor '{0}' has [QuarkStream] attribute but does not implement IStreamConsumer<T>",
        "Quark.Streams",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Actors with [QuarkStream] must implement IStreamConsumer<T> to receive messages.");

    private static readonly DiagnosticDescriptor DuplicateStreamRule = new DiagnosticDescriptor(
        DuplicateStreamDiagnosticId,
        "Duplicate stream subscription",
        "Actor '{0}' has multiple [QuarkStream] attributes with the same namespace '{1}'",
        "Quark.Streams",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Each actor should subscribe to a stream namespace only once.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(InvalidNamespaceRule, MissingInterfaceRule, DuplicateStreamRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null)
            return;

        var quarkStreamAttributes = new System.Collections.Generic.List<(AttributeData attribute, string? @namespace)>();

        // Find all QuarkStream attributes
        foreach (var attribute in classSymbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass == null)
                continue;

            if (attributeClass.ToDisplayString() == "Quark.Abstractions.Streaming.QuarkStreamAttribute")
            {
                // Extract namespace argument
                string? ns = null;
                if (attribute.ConstructorArguments.Length > 0)
                {
                    var arg = attribute.ConstructorArguments[0];
                    if (arg.Value is string s)
                    {
                        ns = s;
                    }
                }

                quarkStreamAttributes.Add((attribute, ns));
            }
        }

        if (quarkStreamAttributes.Count == 0)
            return;

        // Check if class implements IStreamConsumer<T>
        var implementsStreamConsumer = classSymbol.AllInterfaces
            .Any(i => i.Name == "IStreamConsumer" && i.IsGenericType);

        if (!implementsStreamConsumer)
        {
            var diagnostic = Diagnostic.Create(
                MissingInterfaceRule,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }

        // Check for duplicate namespaces
        var namespaces = new System.Collections.Generic.HashSet<string>();
        foreach (var (attribute, ns) in quarkStreamAttributes)
        {
            if (ns == null)
                continue;

            // Validate namespace format
            if (!IsValidNamespace(ns))
            {
                var location = GetAttributeLocation(attribute, classDeclaration, context);
                if (location != null)
                {
                    var diagnostic = Diagnostic.Create(
                        InvalidNamespaceRule,
                        location,
                        ns);
                    context.ReportDiagnostic(diagnostic);
                }
            }

            // Check for duplicates
            if (!namespaces.Add(ns))
            {
                var location = GetAttributeLocation(attribute, classDeclaration, context);
                if (location != null)
                {
                    var diagnostic = Diagnostic.Create(
                        DuplicateStreamRule,
                        location,
                        classSymbol.Name,
                        ns);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool IsValidNamespace(string ns)
    {
        // Valid format: category/subcategory or category/subcategory/detail
        // Must contain at least one '/' and no consecutive slashes
        if (string.IsNullOrWhiteSpace(ns))
            return false;

        if (!ns.Contains("/"))
            return false;

        if (ns.StartsWith("/") || ns.EndsWith("/"))
            return false;

        if (ns.Contains("//"))
            return false;

        return true;
    }

    private static Location? GetAttributeLocation(
        AttributeData attribute,
        ClassDeclarationSyntax classDeclaration,
        SyntaxNodeAnalysisContext context)
    {
        // Try to find the attribute syntax node
        var attributeLists = classDeclaration.AttributeLists;
        foreach (var attributeList in attributeLists)
        {
            foreach (var attr in attributeList.Attributes)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(attr);
                if (symbolInfo.Symbol?.ContainingType?.ToDisplayString() == "Quark.Abstractions.Streaming.QuarkStreamAttribute")
                {
                    return attr.GetLocation();
                }
            }
        }

        return classDeclaration.Identifier.GetLocation();
    }
}
