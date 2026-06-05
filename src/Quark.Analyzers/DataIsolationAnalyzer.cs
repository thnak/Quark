using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
///     Diagnostic analyzer that reports data-isolation behaviour for grain methods:
///     <list type="bullet">
///         <item>QRK0010 (Info) — a parameter will be shallow-cloned (new container, shared element refs).</item>
///         <item>QRK0011 (Warning) — a return type is a mutable reference type with no registered copier.</item>
///     </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DataIsolationAnalyzer : DiagnosticAnalyzer
{
    private const string IGrainFqn = "Quark.Core.Abstractions.Grains.IGrain";
    private const string ImmutableAttributeFqn = "Quark.Core.Abstractions.ImmutableAttribute";
    private const string GenerateSerializerFqn = "Quark.Serialization.Abstractions.Attributes.GenerateSerializerAttribute";

    public static readonly DiagnosticDescriptor ShallowCollectionClone = new(
        "QRK0010",
        "Grain argument will be shallow-cloned for data isolation",
        "Parameter '{0}' of type '{1}' in '{2}' will be shallow-cloned: " +
        "a new collection container is allocated but element references are shared. " +
        "Use value types, strings, or [Immutable] element types for full isolation.",
        "Quark.DataIsolation",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/thnak/Quark/docs/data-isolation.md");

    public static readonly DiagnosticDescriptor FallbackTransportSerialization = new(
        "QRK0012",
        "Grain argument uses boxed fallback for transport serialization",
        "Parameter '{0}' of type '{1}' in '{2}' will use the boxed fallback encoder on the transport path. " +
        "Apply [GenerateSerializer] to generate a zero-boxing codec, or use a primitive, string, byte[], or Guid.",
        "Quark.DataIsolation",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/thnak/Quark/docs/data-isolation.md");

    public static readonly DiagnosticDescriptor UncloneableReturnType = new(
        "QRK0011",
        "Grain return type will not be deep-copied",
        "Return type '{0}' of '{1}' is a mutable reference type. " +
        "No IDeepCopier<{0}> is registered, so the result is returned by reference. " +
        "Add [GenerateSerializer] to generate a copier, or [Immutable] to suppress this warning.",
        "Quark.DataIsolation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/thnak/Quark/docs/data-isolation.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ShallowCollectionClone, UncloneableReturnType, FallbackTransportSerialization);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not IMethodSymbol method)
            return;

        // Only analyze methods declared directly on grain interfaces.
        if (method.ContainingType is not INamedTypeSymbol containingType)
            return;

        bool isGrainInterface = false;
        foreach (INamedTypeSymbol parent in containingType.AllInterfaces)
        {
            if (parent.ToDisplayString() == IGrainFqn)
            {
                isGrainInterface = true;
                break;
            }
        }

        if (!isGrainInterface || containingType.TypeKind != TypeKind.Interface)
            return;

        string methodDisplayName = $"{containingType.Name}.{method.Name}";

        // Check parameters for mutable collections → QRK0010
        // Check parameters for fallback transport serialization → QRK0012
        foreach (IParameterSymbol param in method.Parameters)
        {
            if (IsMutableCollection(param.Type))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    ShallowCollectionClone,
                    param.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault(),
                    param.Name,
                    param.Type.ToDisplayString(),
                    methodDisplayName));
            }

            if (IsTransportSerializationFallback(param.Type))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    FallbackTransportSerialization,
                    param.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault(),
                    param.Name,
                    param.Type.ToDisplayString(),
                    methodDisplayName));
            }
        }

        // Check return type for uncloneable mutable reference types → QRK0011
        ITypeSymbol? returnType = UnwrapTaskResult(method.ReturnType);
        if (returnType is not null && NeedsCloneWarning(returnType))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                UncloneableReturnType,
                method.Locations.FirstOrDefault(),
                returnType.ToDisplayString(),
                methodDisplayName));
        }
    }

    // Returns true when the type falls through to GrainMessageSerializer.WriteValue boxing on the
    // transport path — i.e., it is not a primitive, string, byte[], Guid, or [GenerateSerializer] type.
    private static bool IsTransportSerializationFallback(ITypeSymbol type)
    {
        // Well-known SpecialType primitives + string
        if (type.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Byte    or SpecialType.System_SByte  or
            SpecialType.System_Char    or
            SpecialType.System_Int16   or SpecialType.System_UInt16 or
            SpecialType.System_Int32   or SpecialType.System_UInt32 or
            SpecialType.System_Int64   or SpecialType.System_UInt64 or
            SpecialType.System_Single  or SpecialType.System_Double or
            SpecialType.System_String)
            return false;

        // Guid
        if (type.ToDisplayString() == "System.Guid") return false;

        // byte[] — array of byte
        if (type is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte)
            return false;

        // Collections (element types not checked here; covered separately)
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            string def = named.ConstructedFrom.ToDisplayString();
            if (def is "System.Collections.Generic.List<T>"
                    or "System.Collections.Generic.IList<T>"
                    or "System.Collections.Generic.Dictionary<TKey, TValue>")
                return false;
        }

        if (type is IArrayTypeSymbol) return false;

        // [GenerateSerializer] types have a generated WriteStatic/ReadStatic
        if (HasAttribute(type, GenerateSerializerFqn)) return false;

        // [Immutable] reference types: still need serialization but the developer
        // has opted out of copying, so only warn if it's also not a value type.
        if (type.IsValueType) return false;

        // Reference types without a known codec path
        return type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Interface;
    }

    private static bool IsMutableCollection(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || !named.IsGenericType)
            return false;

        string def = named.ConstructedFrom.ToDisplayString();
        return def is "System.Collections.Generic.List<T>"
                   or "System.Collections.Generic.IList<T>"
                   or "System.Collections.Generic.Dictionary<TKey, TValue>"
            || type is IArrayTypeSymbol;
    }

    private static bool NeedsCloneWarning(ITypeSymbol type)
    {
        // Value types are copied by value — no warning.
        if (type.IsValueType) return false;

        // string is immutable.
        if (type.SpecialType == SpecialType.System_String) return false;

        // [Immutable] — developer explicitly marked it safe.
        if (HasAttribute(type, ImmutableAttributeFqn)) return false;

        // IReadOnly* — mutable operations not possible through the interface.
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            string def = named.ConstructedFrom.ToDisplayString();
            if (def is "System.Collections.Generic.IReadOnlyList<T>"
                    or "System.Collections.Generic.IReadOnlyCollection<T>"
                    or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                    or "System.Collections.Generic.IReadOnlySet<T>")
                return false;
        }

        // [GenerateSerializer] types get a generated IDeepCopier — no warning.
        if (HasAttribute(type, GenerateSerializerFqn)) return false;

        // Everything else is a mutable reference type with no copier registered.
        return type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Interface;
    }

    private static ITypeSymbol? UnwrapTaskResult(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol named || !named.IsGenericType)
            return null;

        string def = named.ConstructedFrom.ToDisplayString();
        if (def is "System.Threading.Tasks.Task<TResult>"
                or "System.Threading.Tasks.ValueTask<TResult>")
            return named.TypeArguments[0];

        return null;
    }

    private static bool HasAttribute(ITypeSymbol type, string fqAttributeName)
    {
        foreach (AttributeData attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == fqAttributeName)
                return true;
        }
        return false;
    }
}
