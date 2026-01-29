using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer for detecting actor classes missing the [Actor] attribute.
/// Classes inheriting from ActorBase should have the [Actor] attribute for proper registration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingActorAttributeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "QUARK005";
    
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Actor class missing [Actor] attribute",
        "Class '{0}' inherits from ActorBase but is missing the [Actor] attribute",
        "Quark.Actors",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Classes inheriting from ActorBase should have the [Actor] attribute for proper factory registration and source generation.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

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

        // Skip abstract classes
        if (classSymbol.IsAbstract)
            return;

        // Check if class already has [Actor] attribute
        var hasActorAttribute = classSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == "Quark.Abstractions.ActorAttribute");

        if (hasActorAttribute)
            return;

        // Check if class inherits from ActorBase
        var baseType = classSymbol.BaseType;
        var inheritsFromActorBase = false;
        
        while (baseType != null)
        {
            var baseTypeName = baseType.Name;
            if (baseTypeName == "ActorBase" || baseTypeName == "StatefulActorBase")
            {
                inheritsFromActorBase = true;
                break;
            }
            baseType = baseType.BaseType;
        }

        if (!inheritsFromActorBase)
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            classDeclaration.Identifier.GetLocation(),
            classSymbol.Name);
        
        context.ReportDiagnostic(diagnostic);
    }
}
