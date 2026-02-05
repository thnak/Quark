using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer that detects unsupported parameter types in actor interface methods.
/// With the binary converter system, actor interfaces only support concrete types (class, struct, record),
/// not delegates, expression trees, or lazy evaluation types (IEnumerable, IAsyncEnumerable).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnsupportedParameterTypeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "QUARK017";
    
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Actor method parameter uses unsupported type",
        "Parameter '{0}' of type '{1}' in actor method '{2}' is not supported. Actor interfaces only support concrete types (class, struct, record, primitives, arrays), not delegates (Action, Func), expression trees (Expression<T>), or lazy types (IEnumerable, IAsyncEnumerable). Consider using a concrete collection type like List<T> or T[] instead.",
        "Quark.Actors",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Actor interface methods must use concrete, serializable types. Delegates (Action, Func), expression trees, and lazy evaluation types (IEnumerable, IAsyncEnumerable) cannot be serialized for distributed actor calls. Use concrete types, arrays, or concrete collections like List<T> instead.");

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

        // Only analyze methods in interfaces that inherit from IQuarkActor
        var containingType = methodSymbol.ContainingType;
        if (containingType == null || containingType.TypeKind != TypeKind.Interface)
            return;

        if (!InheritsFromIQuarkActor(containingType))
            return;

        // Skip special methods
        if (methodSymbol.MethodKind != MethodKind.Ordinary)
            return;

        // Check each parameter
        foreach (var parameter in methodSymbol.Parameters)
        {
            // Skip CancellationToken as it's handled specially
            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
                continue;

            if (IsUnsupportedType(parameter.Type, out var reason))
            {
                var parameterSyntax = methodDeclaration.ParameterList.Parameters
                    .FirstOrDefault(p => p.Identifier.Text == parameter.Name);

                if (parameterSyntax != null)
                {
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        parameterSyntax.GetLocation(),
                        parameter.Name,
                        parameter.Type.ToDisplayString(),
                        methodSymbol.Name);
                    
                    context.ReportDiagnostic(diagnostic);
                }
            }
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

    private static bool IsUnsupportedType(ITypeSymbol type, out string reason)
    {
        reason = string.Empty;
        var typeName = type.ToDisplayString();

        // 1. Detect delegates (Action, Func, custom delegates)
        if (type.TypeKind == TypeKind.Delegate)
        {
            reason = "delegate types cannot be serialized";
            return true;
        }

        // 2. Detect Action and Action<T> explicitly
        if (typeName.StartsWith("System.Action") || typeName == "System.Action")
        {
            reason = "Action delegates cannot be serialized";
            return true;
        }

        // 3. Detect Func<T> and Func<T, TResult> explicitly
        if (typeName.StartsWith("System.Func<"))
        {
            reason = "Func delegates cannot be serialized";
            return true;
        }

        // 4. Detect Expression<T>
        if (typeName.StartsWith("System.Linq.Expressions.Expression<"))
        {
            reason = "expression trees cannot be serialized";
            return true;
        }

        // 5. Detect IEnumerable<T> and IEnumerable (lazy evaluation)
        if (type.TypeKind == TypeKind.Interface)
        {
            // Check if it's IEnumerable or IAsyncEnumerable directly
            if (typeName == "System.Collections.IEnumerable" ||
                typeName.StartsWith("System.Collections.Generic.IEnumerable<") ||
                typeName.StartsWith("System.Collections.Generic.IAsyncEnumerable<"))
            {
                reason = "lazy evaluation types (IEnumerable, IAsyncEnumerable) are not supported";
                return true;
            }

            // Check if it's an interface (other than allowed collection interfaces in concrete scenarios)
            // We allow IList, IDictionary, etc. as parameters if they're returned from methods
            // but discourage general interface usage
            var allowedInterfacePatterns = new[]
            {
                "System.Collections.Generic.IList<",
                "System.Collections.Generic.ICollection<",
                "System.Collections.Generic.IDictionary<",
                "System.Collections.Generic.ISet<",
                "System.Collections.Generic.IReadOnlyList<",
                "System.Collections.Generic.IReadOnlyCollection<",
                "System.Collections.Generic.IReadOnlyDictionary<"
            };

            // If it's not one of the allowed collection interfaces, report it
            var isAllowedInterface = allowedInterfacePatterns.Any(pattern => typeName.StartsWith(pattern));
            
            if (!isAllowedInterface)
            {
                // Check if it's a Quark framework interface (e.g., IQuarkActor) - these are allowed
                if (!typeName.StartsWith("Quark."))
                {
                    reason = "interface types are not supported (use concrete types instead)";
                    return true;
                }
            }
        }

        // 6. Check generic type arguments recursively
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                if (IsUnsupportedType(typeArg, out reason))
                {
                    return true;
                }
            }
        }

        // 7. Check array element types recursively
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsUnsupportedType(arrayType.ElementType, out reason);
        }

        // Everything else is allowed (primitives, classes, structs, records, concrete collections)
        return false;
    }
}
