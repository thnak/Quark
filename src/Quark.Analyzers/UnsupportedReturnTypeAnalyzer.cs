using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer for detecting unsupported return types in IQuarkActor interface methods.
/// IEnumerable and IAsyncEnumerable return types are not supported for actor proxies
/// because they cannot be reliably serialized over the network.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnsupportedReturnTypeAnalyzer : DiagnosticAnalyzer
{
    public const string EnumerableReturnDiagnosticId = "QUARK012";
    public const string EnumerablePropertyDiagnosticId = "QUARK013";
    
    private static readonly DiagnosticDescriptor EnumerableReturnRule = new DiagnosticDescriptor(
        EnumerableReturnDiagnosticId,
        "Actor method returns unsupported IEnumerable or IAsyncEnumerable",
        "Actor method '{0}' returns '{1}' which is not supported for actor proxies. Consider returning Task<List<T>> or Task<T[]> instead.",
        "Quark.Actors",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IEnumerable and IAsyncEnumerable return types cannot be reliably serialized for remote actor calls. Use concrete collection types like List<T> or arrays instead.");

    private static readonly DiagnosticDescriptor EnumerablePropertyRule = new DiagnosticDescriptor(
        EnumerablePropertyDiagnosticId,
        "Actor interface property exposes unsupported IEnumerable or IAsyncEnumerable",
        "Actor interface property '{0}' exposes '{1}' which is not supported for actor proxies. Consider using a concrete collection type like List<T> or T[] instead, or make it an internal field.",
        "Quark.Actors",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Properties with IEnumerable or IAsyncEnumerable types cannot be reliably serialized for remote actor calls. Use concrete collection types or internal fields instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EnumerableReturnRule, EnumerablePropertyRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return;

        // Only analyze methods in interfaces that inherit from IQuarkActor
        var containingType = methodSymbol.ContainingType;
        if (containingType == null || containingType.TypeKind != TypeKind.Interface)
            return;

        if (!InheritsFromIQuarkActor(containingType))
            return;

        // Skip special methods
        if (methodSymbol.MethodKind != MethodKind.Ordinary)
            return;

        // Check return type
        var returnType = methodSymbol.ReturnType;
        
        // Unwrap Task<T> or ValueTask<T>
        var actualReturnType = UnwrapTaskType(returnType);
        if (actualReturnType == null)
            return;

        // Check if the return type is IEnumerable, IAsyncEnumerable, or their generic versions
        if (IsUnsupportedEnumerableType(actualReturnType, out var typeName))
        {
            var diagnostic = Diagnostic.Create(
                EnumerableReturnRule,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name,
                typeName);
            
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);

        if (propertySymbol == null)
            return;

        // Only analyze properties in interfaces that inherit from IQuarkActor
        var containingType = propertySymbol.ContainingType;
        if (containingType == null || containingType.TypeKind != TypeKind.Interface)
            return;

        if (!InheritsFromIQuarkActor(containingType))
            return;

        // Check property type
        var propertyType = propertySymbol.Type;

        // Check if the property type is IEnumerable, IAsyncEnumerable, or their generic versions
        if (IsUnsupportedEnumerableType(propertyType, out var typeName))
        {
            var diagnostic = Diagnostic.Create(
                EnumerablePropertyRule,
                propertyDeclaration.Identifier.GetLocation(),
                propertySymbol.Name,
                typeName);
            
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool InheritsFromIQuarkActor(INamedTypeSymbol interfaceSymbol)
    {
        // Check if this interface directly inherits from IQuarkActor
        foreach (var iface in interfaceSymbol.Interfaces)
        {
            var displayString = iface.ToDisplayString();
            if (displayString == "Quark.Abstractions.IQuarkActor")
                return true;
        }

        // Check all interfaces (including transitive)
        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            if (baseInterface.ToDisplayString() == "Quark.Abstractions.IQuarkActor")
                return true;
        }

        return false;
    }

    private static ITypeSymbol? UnwrapTaskType(ITypeSymbol returnType)
    {
        // If it's Task or ValueTask without result, return null (void-like)
        var returnTypeName = returnType.ToDisplayString();
        if (returnTypeName == "System.Threading.Tasks.Task" || 
            returnTypeName == "System.Threading.Tasks.ValueTask")
        {
            return null;
        }

        // If it's Task<T> or ValueTask<T>, unwrap to get T
        if (returnType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var typeDefinition = namedType.ConstructedFrom.ToDisplayString();
            if (typeDefinition == "System.Threading.Tasks.Task<TResult>" ||
                typeDefinition == "System.Threading.Tasks.ValueTask<TResult>")
            {
                return namedType.TypeArguments[0];
            }
        }

        // For non-async methods, return the type as-is
        return returnType;
    }

    private static bool IsUnsupportedEnumerableType(ITypeSymbol type, out string typeName)
    {
        typeName = type.ToDisplayString();

        // Check for IEnumerable<T> or IEnumerable
        if (typeName.StartsWith("System.Collections.Generic.IEnumerable<") ||
            typeName == "System.Collections.IEnumerable")
        {
            return true;
        }

        // Check for IAsyncEnumerable<T>
        if (typeName.StartsWith("System.Collections.Generic.IAsyncEnumerable<"))
        {
            return true;
        }

        // Check if the type implements IEnumerable or IAsyncEnumerable
        if (type is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces)
            {
                var ifaceName = iface.ToDisplayString();
                if (ifaceName.StartsWith("System.Collections.Generic.IEnumerable<") ||
                    ifaceName == "System.Collections.IEnumerable" ||
                    ifaceName.StartsWith("System.Collections.Generic.IAsyncEnumerable<"))
                {
                    // Allow concrete collection types like List<T>, T[], etc.
                    // Only block if the type itself is IEnumerable/IAsyncEnumerable
                    if (type.TypeKind == TypeKind.Interface)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
