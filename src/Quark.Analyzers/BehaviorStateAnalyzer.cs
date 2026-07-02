using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
///     Warns when a grain behavior class stores mutable per-instance state in fields or
///     writable auto-properties that survive only for the duration of one call.
///     <list type="bullet">
///         <item>QRK0020 (Warning) — non-readonly instance field on a behavior class.</item>
///         <item>QRK0021 (Warning) — writable auto-property on a behavior class.</item>
///         <item>QRK0022 (Warning) — mutable static field/property on a behavior class.</item>
///     </list>
/// </summary>
/// <remarks>
///     Quark constructs a fresh <c>IGrainBehavior</c> instance per grain method invocation.
///     Any value written to an instance field or property during call N is gone by call N+1.
///     Cross-call state must be stored in <c>IActivationMemory&lt;T&gt;</c> (in-memory),
///     <c>IManagedActivationMemory&lt;T&gt;</c> (async-init resources, e.g. timer handles),
///     or <c>IPersistentActivationMemory&lt;T&gt;</c> (durable storage).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BehaviorStateAnalyzer : DiagnosticAnalyzer
{
    private const string IGrainBehaviorFqn = "Quark.Core.Abstractions.Grains.IGrainBehavior";

    public static readonly DiagnosticDescriptor MutableInstanceField = new(
        "QRK0020",
        "Mutable instance field on grain behavior will be reset between calls",
        "Field '{0}' on grain behavior '{1}' is mutable. " +
        "Each grain method call constructs a new behavior instance, so this field's value " +
        "is discarded after every call. " +
        "Store cross-call state in IActivationMemory<T> or IManagedActivationMemory<T> instead.",
        "Quark.BehaviorLifecycle",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/thnak/Quark/wiki/Timers-and-Reminders#registration");

    public static readonly DiagnosticDescriptor WritableAutoProperty = new(
        "QRK0021",
        "Writable auto-property on grain behavior will be reset between calls",
        "Property '{0}' on grain behavior '{1}' has a writable setter. " +
        "Each grain method call constructs a new behavior instance, so any value assigned " +
        "to this property is lost after the call completes. " +
        "Store cross-call state in IActivationMemory<T> or IManagedActivationMemory<T> instead.",
        "Quark.BehaviorLifecycle",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/thnak/Quark/wiki/Timers-and-Reminders#registration");

    public static readonly DiagnosticDescriptor MutableStaticState = new(
        "QRK0022",
        "Static mutable state on grain behavior is shared across all activations",
        "Static mutable state on grain behavior '{1}' (member '{0}') is shared across all activations on this " +
        "silo and is not part of the engine-owned state model. " +
        "Use IActivationMemory<T> (per-activation), a registered singleton service, or persistent state.",
        "Quark.BehaviorLifecycle",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/thnak/Quark/wiki/AOT-and-Trim");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MutableInstanceField, WritableAutoProperty, MutableStaticState);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not INamedTypeSymbol type)
            return;

        if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            return;

        bool isGrainBehavior = false;
        foreach (INamedTypeSymbol iface in type.AllInterfaces)
        {
            if (iface.ToDisplayString() == IGrainBehaviorFqn)
            {
                isGrainBehavior = true;
                break;
            }
        }

        if (!isGrainBehavior)
            return;

        foreach (ISymbol member in type.GetMembers())
        {
            // Non-static, non-const, non-readonly instance fields → QRK0020
            if (member is IFieldSymbol field &&
                !field.IsStatic &&
                !field.IsConst &&
                !field.IsReadOnly)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MutableInstanceField,
                    field.Locations.FirstOrDefault() ?? Location.None,
                    field.Name,
                    type.Name));
            }

            // Auto-properties with a non-init writable setter → QRK0021
            if (member is IPropertySymbol property &&
                !property.IsStatic &&
                !property.IsReadOnly &&
                property.SetMethod is { } setter &&
                setter.IsInitOnly == false &&
                IsAutoProperty(property))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    WritableAutoProperty,
                    property.Locations.FirstOrDefault() ?? Location.None,
                    property.Name,
                    type.Name));
            }

            // Static fields that are neither const nor provably immutable → QRK0022.
            // readonly-ness alone isn't enough: a `static readonly Dictionary<,>` still
            // lets every activation mutate the same shared instance.
            if (member is IFieldSymbol staticField &&
                staticField.IsStatic &&
                !staticField.IsConst &&
                !(staticField.IsReadOnly && IsDeeplyImmutableType(staticField.Type)))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MutableStaticState,
                    staticField.Locations.FirstOrDefault() ?? Location.None,
                    staticField.Name,
                    type.Name));
            }

            // Writable static auto-properties → QRK0022.
            if (member is IPropertySymbol staticProperty &&
                staticProperty.IsStatic &&
                staticProperty.SetMethod is { } staticSetter &&
                staticSetter.IsInitOnly == false &&
                IsAutoProperty(staticProperty))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MutableStaticState,
                    staticProperty.Locations.FirstOrDefault() ?? Location.None,
                    staticProperty.Name,
                    type.Name));
            }
        }
    }

    // Types whose value can never change after construction, so a `static readonly` field
    // of this type cannot become a shared-mutable-state footgun.
    private static bool IsDeeplyImmutableType(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_Char:
            case SpecialType.System_String:
                return true;
        }

        if (type.TypeKind == TypeKind.Enum)
            return true;

        string fqn = type.OriginalDefinition.ToDisplayString();
        return fqn is "System.Guid" or "System.TimeSpan" or "System.DateTime" or "System.DateTimeOffset"
            || fqn.StartsWith("System.Collections.Immutable.", StringComparison.Ordinal);
    }

    // An auto-property has no explicit getter/setter body — its backing field is compiler-generated.
    private static bool IsAutoProperty(IPropertySymbol property)
    {
        // GetMethod being null means the property has no getter at all.
        // For auto-properties the getter body is synthesized; for explicitly-implemented
        // ones it has user-authored syntax. We distinguish by checking whether the
        // backing field (always named "<PropName>k__BackingField") is present.
        foreach (ISymbol sibling in property.ContainingType.GetMembers())
        {
            if (sibling is IFieldSymbol field &&
                field.IsImplicitlyDeclared &&
                field.AssociatedSymbol?.Equals(property, SymbolEqualityComparer.Default) == true)
                return true;
        }
        return false;
    }
}
