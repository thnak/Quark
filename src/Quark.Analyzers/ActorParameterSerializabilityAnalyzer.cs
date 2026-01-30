using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer for checking if actor method parameters are serializable.
/// Actor methods should use types that can be serialized for distributed calls.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ActorParameterSerializabilityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "QUARK006";
    
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Actor method parameter may not be serializable",
        "Parameter '{0}' of type '{1}' in actor method may not be JSON serializable for distributed calls",
        "Quark.Actors",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Actor method parameters should use types that can be JSON serialized for distributed calls. Avoid delegates, interfaces (except common ones), and complex types without proper serialization attributes.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return;

        // Only analyze public or internal methods in actor classes
        if (methodSymbol.DeclaredAccessibility != Accessibility.Public &&
            methodSymbol.DeclaredAccessibility != Accessibility.Internal)
            return;

        // Skip special methods
        if (methodSymbol.MethodKind != MethodKind.Ordinary)
            return;

        // Check if the containing class has [Actor] attribute or inherits from ActorBase
        var containingClass = methodSymbol.ContainingType;
        if (containingClass == null)
            return;

        var hasActorAttribute = containingClass.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == "Quark.Abstractions.ActorAttribute");

        if (!hasActorAttribute)
        {
            // Also check if it inherits from ActorBase
            var baseType = containingClass.BaseType;
            var isActorClass = false;
            while (baseType != null)
            {
                if (baseType.Name == "ActorBase" || baseType.Name == "StatefulActorBase")
                {
                    isActorClass = true;
                    break;
                }
                baseType = baseType.BaseType;
            }

            if (!isActorClass)
                return;
        }

        // Check each parameter
        foreach (var parameter in methodSymbol.Parameters)
        {
            // Skip CancellationToken as it's handled specially
            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
                continue;

            if (!IsLikelySerializable(parameter.Type))
            {
                var parameterSyntax = methodDeclaration.ParameterList.Parameters
                    .FirstOrDefault(p => p.Identifier.Text == parameter.Name);

                if (parameterSyntax != null)
                {
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        parameterSyntax.GetLocation(),
                        parameter.Name,
                        parameter.Type.ToDisplayString());
                    
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool IsLikelySerializable(ITypeSymbol type)
    {
        var typeName = type.ToDisplayString();

        // Primitive types are serializable
        if (type.SpecialType != SpecialType.None && type.SpecialType != SpecialType.System_Object)
            return true;

        // Common serializable types
        if (typeName == "string" || 
            typeName == "System.String" ||
            typeName == "System.DateTime" ||
            typeName == "System.DateTimeOffset" ||
            typeName == "System.TimeSpan" ||
            typeName == "System.Guid" ||
            typeName == "System.Uri" ||
            typeName == "System.Decimal")
            return true;

        // Arrays of serializable types
        if (type is IArrayTypeSymbol arrayType)
            return IsLikelySerializable(arrayType.ElementType);

        // Generic collections (List<T>, Dictionary<K,V>, etc.)
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var genericName = namedType.ConstructedFrom.ToDisplayString();
            
            // Common collection types
            if (genericName.StartsWith("System.Collections.Generic.List<") ||
                genericName.StartsWith("System.Collections.Generic.Dictionary<") ||
                genericName.StartsWith("System.Collections.Generic.HashSet<") ||
                genericName.StartsWith("System.Collections.Generic.IEnumerable<") ||
                genericName.StartsWith("System.Collections.Generic.IList<") ||
                genericName.StartsWith("System.Collections.Generic.IDictionary<") ||
                genericName.StartsWith("System.Collections.Generic.ICollection<") ||
                genericName.StartsWith("System.Collections.Generic.IReadOnlyList<") ||
                genericName.StartsWith("System.Collections.Generic.IReadOnlyCollection<") ||
                genericName.StartsWith("System.Collections.Generic.IReadOnlyDictionary<"))
            {
                // Check if type arguments are serializable
                return namedType.TypeArguments.All(IsLikelySerializable);
            }

            // Task types are not parameters that need serialization
            if (genericName.StartsWith("System.Threading.Tasks.Task<") ||
                genericName.StartsWith("System.Threading.Tasks.ValueTask<"))
                return true;
        }

        // Delegates are not serializable
        if (type.TypeKind == TypeKind.Delegate)
            return false;

        // Interfaces (except common serializable ones) are problematic
        if (type.TypeKind == TypeKind.Interface)
        {
            // Allow some common interfaces
            var interfaceName = type.ToDisplayString();
            if (interfaceName.StartsWith("System.Collections.Generic.I"))
                return true;
            
            return false;
        }

        // Classes/structs - assume serializable if they're not interfaces or delegates
        // In reality, this is optimistic, but we can't easily check without runtime info
        if (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct)
        {
            // Check if it has [Serializable] or JSON serialization attributes (if available)
            // For now, we'll be optimistic about classes/structs
            return true;
        }

        // Unknown types - warn to be safe
        return false;
    }
}
