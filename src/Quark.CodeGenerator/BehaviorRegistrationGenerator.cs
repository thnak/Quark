using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Quark.CodeGenerator;

/// <summary>
///     Generates a per-assembly <c>QuarkRegistrations</c> partial class with an
///     <c>Add{AssemblyName}Behaviors(IServiceCollection)</c> extension method that registers every
///     <see cref="Quark.Core.Abstractions.Grains.IGrainBehavior"/> class found in the assembly:
///     <list type="bullet">
///         <item><c>AddGrainBehavior&lt;TInterface, TBehavior&gt;()</c></item>
///         <item><c>AddGrainTransportDispatcher(grainType, Proxy_TransportDispatcher.Instance)</c></item>
///         <item>Scoped <c>IActivationMemory&lt;T&gt;</c> accessor per distinct TState</item>
///         <item>Scoped <c>IPersistentActivationMemory&lt;T&gt;</c> accessor per distinct TState</item>
///         <item>Scoped <c>IManagedActivationMemory&lt;T&gt;</c> accessor per distinct resource type</item>
///         <item>Scoped <c>IEagerActivationMemory&lt;T&gt;</c> accessor per distinct resource type</item>
///         <item>Scoped <c>IPersistentState&lt;T&gt;</c> via <c>[PersistentState("name","provider")]</c></item>
///         <item><c>AddImplicitStreamSubscription(namespace, grainType)</c> per <c>[ImplicitStreamSubscription]</c></item>
///     </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class BehaviorRegistrationGenerator : IIncrementalGenerator
{
    private const string IGrainBehaviorFqn = "Quark.Core.Abstractions.Grains.IGrainBehavior";
    private const string IGrainFqn = "Quark.Core.Abstractions.Grains.IGrain";
    private const string GrainBehaviorAttributeFqn = "Quark.Core.Abstractions.Grains.GrainBehaviorAttribute";
    private const string IGrainUserServiceProviderFactoryFqn = "Quark.Core.Abstractions.Hosting.IGrainUserServiceProviderFactory";
    private const string IActivationMemoryNs = "Quark.Core.Abstractions.Hosting";
    private const string IActivationMemoryName = "IActivationMemory";
    private const string IPersistentActivationMemoryNs = "Quark.Persistence.Abstractions";
    private const string IPersistentActivationMemoryName = "IPersistentActivationMemory";
    private const string IManagedActivationMemoryNs = "Quark.Core.Abstractions.Hosting";
    private const string IManagedActivationMemoryName = "IManagedActivationMemory";
    private const string IEagerActivationMemoryNs = "Quark.Core.Abstractions.Hosting";
    private const string IEagerActivationMemoryName = "IEagerActivationMemory";
    private const string ImplicitStreamSubscriptionAttributeFqn = "Quark.Streaming.Abstractions.ImplicitStreamSubscriptionAttribute";
    private const string InMemoryStreamingExtensionsFqn = "Quark.Streaming.InMemory.InMemoryStreamingServiceCollectionExtensions";
    private const string IPersistentStateNs = "Quark.Persistence.Abstractions";
    private const string IPersistentStateName = "IPersistentState";
    private const string PersistentStateAttributeFqn = "Quark.Persistence.Abstractions.PersistentStateAttribute";
    private const string DefaultStorageName = "Default";
    private const string PreferLocalPlacementAttributeFqn = "Quark.Core.Abstractions.Placement.PreferLocalPlacementAttribute";
    private const string LocalPlacementAttributeFqn = "Quark.Core.Abstractions.Placement.LocalPlacementAttribute";
    private const string HashBasedPlacementAttributeFqn = "Quark.Core.Abstractions.Placement.HashBasedPlacementAttribute";
    private const string StatelessWorkerAttributeFqn = "Quark.Core.Abstractions.Placement.StatelessWorkerAttribute";

    internal static readonly DiagnosticDescriptor MissingGrainInterface = new(
        id: "QRK0050",
        title: "Behavior missing grain interface",
        messageFormat: "'{0}' implements IGrainBehavior but no IGrain-derived interface. Add [GrainBehavior(\"typeName\")] or implement the grain interface.",
        category: "Quark.CodeGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AmbiguousGrainInterface = new(
        id: "QRK0051",
        title: "Behavior implements multiple grain interfaces",
        messageFormat: "'{0}' implements multiple IGrain-derived interfaces. The first ('{1}') is used. Add a single grain interface to silence.",
        category: "Quark.CodeGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ConflictingPersistentStateSlots = new(
        id: "QRK0052",
        title: "Conflicting IPersistentState<T> state names",
        messageFormat: "'{0}' is used as IPersistentState<T> with different (stateName, providerName) combinations in this assembly. Use distinct state types for each logical slot.",
        category: "Quark.CodeGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ImplicitStreamSubscriptionNoProvider = new(
        id: "QRK0053",
        title: "[ImplicitStreamSubscription] auto-registration skipped",
        messageFormat: "'{0}' is marked [ImplicitStreamSubscription] but this assembly does not reference Quark.Streaming.InMemory. Auto-registration was skipped — reference the package or call AddImplicitStreamSubscription(...) manually.",
        category: "Quark.CodeGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AmbiguousBehaviorConstructor = new(
        id: "QRK0055",
        title: "Cannot generate compile-time factory for behavior",
        messageFormat: "'{0}' does not have exactly one public constructor with only required parameters, " +
                       "so a compile-time factory cannot be generated. The behavior will be constructed via " +
                       "a runtime-reflection fallback (ActivatorUtilities) instead of the AOT-safe generated factory.",
        category: "Quark.CodeGenerator",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<string?> assemblyName = context.CompilationProvider
            .Select(static (c, _) => c.AssemblyName);

        IncrementalValuesProvider<BehaviorModel?> models = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                ExtractModel)
            .Where(static m => m is not null);

        context.RegisterSourceOutput(
            assemblyName.Combine(models.Collect()),
            static (ctx, pair) => Emit(ctx, pair.Left, pair.Right!));
    }

    // -----------------------------------------------------------------------
    // Model extraction
    // -----------------------------------------------------------------------

    private static BehaviorModel? ExtractModel(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ClassDeclarationSyntax) return null;
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol type) return null;

        if (type.IsAbstract) return null;
        if (type.TypeParameters.Length > 0) return null;
        if (type.DeclaredAccessibility == Accessibility.Private) return null;

        if (!type.AllInterfaces.Any(static i => i.ToDisplayString() == IGrainBehaviorFqn)) return null;

        ct.ThrowIfCancellationRequested();

        // Find IGrain-derived interfaces among directly-implemented interfaces.
        var grainIfaces = new List<INamedTypeSymbol>();
        foreach (INamedTypeSymbol iface in type.Interfaces)
        {
            if (iface.AllInterfaces.Any(static i => i.ToDisplayString() == IGrainFqn))
                grainIfaces.Add(iface);
        }

        if (grainIfaces.Count == 0)
        {
            Location location = type.Locations.FirstOrDefault() ?? Location.None;
            return new BehaviorModel(
                diagnostics: ImmutableArray.Create(
                    Diagnostic.Create(MissingGrainInterface, location, type.Name)));
        }

        INamedTypeSymbol grainIface = grainIfaces[0];

        var diagList = new List<Diagnostic>();
        if (grainIfaces.Count > 1)
        {
            Location location = type.Locations.FirstOrDefault() ?? Location.None;
            diagList.Add(
                Diagnostic.Create(AmbiguousGrainInterface, location, type.Name, grainIface.Name));
        }

        bool implementsUserServiceProviderFactory = type.AllInterfaces.Any(
            static i => i.ToDisplayString() == IGrainUserServiceProviderFactoryFqn);

        string behaviorNs = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();
        string behaviorFqn = string.IsNullOrEmpty(behaviorNs)
            ? $"global::{type.Name}"
            : $"global::{behaviorNs}.{type.Name}";

        // Placement strategy: mirrors AttributePlacementStrategyResolver.ResolveCore's exact precedence
        // (PreferLocal > Local > HashBased > StatelessWorker > Random).
        ImmutableArray<AttributeData> classAttributes = type.GetAttributes();
        string placementStrategyExpression;
        if (classAttributes.Any(a => a.AttributeClass?.ToDisplayString() == PreferLocalPlacementAttributeFqn))
        {
            placementStrategyExpression = "global::Quark.Core.Abstractions.Placement.PreferLocalPlacement.Singleton";
        }
        else if (classAttributes.Any(a => a.AttributeClass?.ToDisplayString() == LocalPlacementAttributeFqn))
        {
            placementStrategyExpression = "global::Quark.Core.Abstractions.Placement.LocalPlacement.Singleton";
        }
        else if (classAttributes.Any(a => a.AttributeClass?.ToDisplayString() == HashBasedPlacementAttributeFqn))
        {
            placementStrategyExpression = "global::Quark.Core.Abstractions.Placement.HashBasedPlacement.Singleton";
        }
        else
        {
            AttributeData? statelessAttr = classAttributes
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == StatelessWorkerAttributeFqn);
            if (statelessAttr is not null)
            {
                int maxLocalWorkers = statelessAttr.ConstructorArguments.Length > 0 &&
                                       statelessAttr.ConstructorArguments[0].Value is int m
                    ? m
                    : -1;
                placementStrategyExpression =
                    $"new global::Quark.Core.Abstractions.Placement.StatelessWorkerPlacement({maxLocalWorkers})";
            }
            else
            {
                placementStrategyExpression = "global::Quark.Core.Abstractions.Placement.RandomPlacement.Singleton";
            }
        }

        // Compile-time construction factory: requires exactly one public, non-static constructor with
        // only required parameters. Anything else falls back to the runtime reflection path and warns.
        string? factoryExpression = null;
        Diagnostic? factoryDiagnostic = null;
        ImmutableArray<IMethodSymbol> publicCtors = type.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public)
            .ToImmutableArray();

        if (publicCtors.Length == 1 && publicCtors[0].Parameters.All(static p => !p.HasExplicitDefaultValue))
        {
            IMethodSymbol ctor = publicCtors[0];
            var argExprs = new List<string>();
            foreach (IParameterSymbol p in ctor.Parameters)
            {
                string paramFqn = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                argExprs.Add($"sp.GetRequiredService<{paramFqn}>()");
            }
            factoryExpression = $"static sp => new {behaviorFqn}({string.Join(", ", argExprs)})";
        }
        else
        {
            Location ctorLocation = type.Locations.FirstOrDefault() ?? Location.None;
            factoryDiagnostic = Diagnostic.Create(AmbiguousBehaviorConstructor, ctorLocation, type.Name);
        }

        if (factoryDiagnostic is not null)
        {
            diagList.Add(factoryDiagnostic);
        }

        string grainTypeName = GetGrainTypeName(type, grainIface);

        string ifaceNs = grainIface.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : grainIface.ContainingNamespace.ToDisplayString();
        string ifaceShortName = grainIface.Name;
        string proxyShort = ifaceShortName.StartsWith("I", StringComparison.Ordinal)
            ? ifaceShortName.Substring(1) + "Proxy"
            : ifaceShortName + "Proxy";
        string proxyFqn = string.IsNullOrEmpty(ifaceNs)
            ? $"global::{proxyShort}"
            : $"global::{ifaceNs}.{proxyShort}";
        string grainIfaceFqn = string.IsNullOrEmpty(ifaceNs)
            ? $"global::{ifaceShortName}"
            : $"global::{ifaceNs}.{ifaceShortName}";

        var inMemory = new List<string>();
        var persistent = new List<string>();
        var managed = new List<string>();
        var eager = new List<string>();
        var persistentSlots = new List<PersistentStateSlot>();

        foreach (IMethodSymbol ctor in type.Constructors)
        {
            if (ctor.IsStatic) continue;
            foreach (IParameterSymbol param in ctor.Parameters)
            {
                ct.ThrowIfCancellationRequested();
                if (param.Type is not INamedTypeSymbol named || !named.IsGenericType || named.TypeArguments.Length != 1)
                    continue;

                string paramNs = named.ContainingNamespace.ToDisplayString();
                string tArgFqn = named.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (paramNs == IActivationMemoryNs && named.Name == IActivationMemoryName)
                    inMemory.Add(tArgFqn);
                else if (paramNs == IPersistentActivationMemoryNs && named.Name == IPersistentActivationMemoryName)
                    persistent.Add(tArgFqn);
                else if (paramNs == IManagedActivationMemoryNs && named.Name == IManagedActivationMemoryName)
                    managed.Add(tArgFqn);
                else if (paramNs == IEagerActivationMemoryNs && named.Name == IEagerActivationMemoryName)
                    eager.Add(tArgFqn);
                else if (paramNs == IPersistentStateNs && named.Name == IPersistentStateName)
                {
                    foreach (AttributeData attr in param.GetAttributes())
                    {
                        if (attr.AttributeClass?.ToDisplayString() != PersistentStateAttributeFqn) continue;
                        string stateName = attr.ConstructorArguments.Length > 0
                            ? (string?)attr.ConstructorArguments[0].Value ?? string.Empty
                            : string.Empty;
                        string providerName = attr.ConstructorArguments.Length > 1
                            ? (string?)attr.ConstructorArguments[1].Value ?? DefaultStorageName
                            : DefaultStorageName;
                        persistentSlots.Add(new PersistentStateSlot(tArgFqn, stateName, providerName));
                        break;
                    }
                }
            }
        }

        // [ImplicitStreamSubscription("ns")] on the behavior class (AllowMultiple = true).
        var implicitNamespaces = new List<string>();
        foreach (AttributeData attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ImplicitStreamSubscriptionAttributeFqn &&
                attr.ConstructorArguments.Length == 1 &&
                attr.ConstructorArguments[0].Value is string ns &&
                !string.IsNullOrWhiteSpace(ns))
            {
                implicitNamespaces.Add(ns);
            }
        }

        // AddImplicitStreamSubscription lives in Quark.Streaming.InMemory. If the attribute is
        // present but that package isn't referenced, we cannot emit a compilable call — warn and skip.
        if (implicitNamespaces.Count > 0 &&
            ctx.SemanticModel.Compilation.GetTypeByMetadataName(InMemoryStreamingExtensionsFqn) is null)
        {
            Location loc = type.Locations.FirstOrDefault() ?? Location.None;
            diagList.Add(Diagnostic.Create(ImplicitStreamSubscriptionNoProvider, loc, type.Name));
            implicitNamespaces.Clear();
        }

        return new BehaviorModel(
            behaviorFqn: behaviorFqn,
            grainInterfaceFqn: grainIfaceFqn,
            grainTypeName: grainTypeName,
            proxyFqn: proxyFqn,
            placementStrategyExpression: placementStrategyExpression,
            factoryExpression: factoryExpression,
            inMemoryStateTypes: inMemory.Distinct().ToImmutableArray(),
            persistentStateTypes: persistent.Distinct().ToImmutableArray(),
            managedStateTypes: managed.Distinct().ToImmutableArray(),
            eagerStateTypes: eager.Distinct().ToImmutableArray(),
            implicitStreamNamespaces: implicitNamespaces.Distinct().ToImmutableArray(),
            persistentStateSlots: persistentSlots.Distinct().ToImmutableArray(),
            implementsUserServiceProviderFactory: implementsUserServiceProviderFactory,
            diagnostics: diagList.ToImmutableArray());
    }

    private static string GetGrainTypeName(INamedTypeSymbol behavior, INamedTypeSymbol grainIface)
    {
        foreach (AttributeData attr in behavior.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == GrainBehaviorAttributeFqn &&
                attr.ConstructorArguments.Length == 1 &&
                attr.ConstructorArguments[0].Value is string key &&
                !string.IsNullOrWhiteSpace(key))
            {
                return key;
            }
        }

        string name = grainIface.Name;
        return name.StartsWith("I", StringComparison.Ordinal) ? name.Substring(1) : name;
    }

    // -----------------------------------------------------------------------
    // Code emission
    // -----------------------------------------------------------------------

    private static void Emit(
        SourceProductionContext ctx,
        string? assemblyName,
        ImmutableArray<BehaviorModel> models)
    {
        if (models.IsDefaultOrEmpty) return;

        var valid = new List<BehaviorModel>();
        foreach (BehaviorModel m in models)
        {
            foreach (Diagnostic d in m.Diagnostics)
                ctx.ReportDiagnostic(d);

            if (m.IsValid)
                valid.Add(m);
        }

        if (valid.Count == 0) return;

        string methodName = SanitizeAssemblyName(assemblyName ?? "Unknown");

        List<string> inMemoryStates = valid
            .SelectMany(static m => m.InMemoryStateTypes)
            .Distinct()
            .OrderBy(static s => s)
            .ToList();
        List<string> persistentStates = valid
            .SelectMany(static m => m.PersistentStateTypes)
            .Distinct()
            .OrderBy(static s => s)
            .ToList();
        List<string> managedStates = valid
            .SelectMany(static m => m.ManagedStateTypes)
            .Distinct()
            .OrderBy(static s => s)
            .ToList();
        List<string> eagerStates = valid
            .SelectMany(static m => m.EagerStateTypes)
            .Distinct()
            .OrderBy(static s => s)
            .ToList();
        List<(string Namespace, string GrainTypeKey)> implicitSubscriptions = valid
            .SelectMany(static m => m.ImplicitStreamNamespaces
                .Select(ns => (Namespace: ns, GrainTypeKey: m.GrainTypeName)))
            .Distinct()
            .OrderBy(static x => x.Namespace, StringComparer.Ordinal)
            .ThenBy(static x => x.GrainTypeKey, StringComparer.Ordinal)
            .ToList();

        // Collect [PersistentState] IPersistentState<T> slots; detect conflicts (same T, different name/provider).
        List<PersistentStateSlot> persistentSlots = new();
        foreach (IGrouping<string, PersistentStateSlot> group in valid
                     .SelectMany(static m => m.PersistentStateSlots)
                     .Distinct()
                     .GroupBy(static s => s.TArgFqn)
                     .OrderBy(static g => g.Key))
        {
            List<PersistentStateSlot> distinct = group.Distinct().ToList();
            if (distinct.Count > 1)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(ConflictingPersistentStateSlots, Location.None, group.Key));
            }
            else
            {
                persistentSlots.Add(distinct[0]);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by Quark.CodeGenerator — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Quark.CodeGenerator\", \"0.1.0\")]");
        sb.AppendLine("public static partial class QuarkRegistrations");
        sb.AppendLine("{");
        sb.AppendLine($"    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {methodName}(");
        sb.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (BehaviorModel m in valid)
        {
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddGrainBehavior<{m.GrainInterfaceFqn}, {m.BehaviorFqn}>(");
            sb.AppendLine("            services,");
            sb.AppendLine($"            behaviorId: \"{m.GrainTypeName}\",");
            sb.AppendLine(m.FactoryExpression is not null
                ? $"            factory: {m.FactoryExpression});"
                : "            factory: null);");
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddGrainPlacementStrategy<{m.BehaviorFqn}>(");
            sb.AppendLine($"            services, {m.PlacementStrategyExpression});");
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddGrainTransportDispatcher(services,");
            sb.AppendLine($"            new global::Quark.Core.Abstractions.Identity.GrainType(\"{m.GrainTypeName}\"),");
            sb.AppendLine($"            {m.ProxyFqn}_TransportDispatcher.Instance);");

            if (m.ImplementsUserServiceProviderFactory)
            {
                sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddGrainUserServiceProviderFactory<{m.GrainInterfaceFqn}, {m.BehaviorFqn}>(");
                sb.AppendLine($"            services, behaviorId: \"{m.GrainTypeName}\");");
            }
        }

        if (inMemoryStates.Count > 0) sb.AppendLine();
        foreach (string tArg in inMemoryStates)
        {
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddQuarkOwnedScoped<global::Quark.Core.Abstractions.Hosting.IActivationMemory<{tArg}>>(services, static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.ActivationMemoryAccessor<{tArg}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>()");
            sb.AppendLine($"                  .Shell.GetOrCreateHolder<{tArg}>()));");
        }

        if (persistentStates.Count > 0) sb.AppendLine();
        foreach (string tArg in persistentStates)
        {
            sb.AppendLine($"        services.AddScoped<global::Quark.Persistence.Abstractions.IPersistentActivationMemory<{tArg}>>(static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.PersistentActivationMemoryAccessor<{tArg}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>()");
            sb.AppendLine($"                  .Shell.GetOrCreateHolder<{tArg}>(),");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Persistence.Abstractions.IStorage<{tArg}>>(),");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Core.Abstractions.Hosting.ICallContext>(),");
            sb.AppendLine($"                global::Quark.Persistence.Abstractions.StorageOptions.DefaultStateName));");
        }

        if (managedStates.Count > 0) sb.AppendLine();
        foreach (string tArg in managedStates)
        {
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddQuarkOwnedScoped<global::Quark.Core.Abstractions.Hosting.IManagedActivationMemory<{tArg}>>(services, static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.ManagedActivationMemoryAccessor<{tArg}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>()");
            sb.AppendLine($"                  .Shell.GetOrCreateManagedHolder<{tArg}>()));");
        }

        if (eagerStates.Count > 0) sb.AppendLine();
        foreach (string tArg in eagerStates)
        {
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddEagerActivationMemory<{tArg}>(services);");
        }

        if (implicitSubscriptions.Count > 0) sb.AppendLine();
        foreach ((string ns, string grainTypeKey) in implicitSubscriptions)
        {
            sb.AppendLine($"        global::Quark.Streaming.InMemory.InMemoryStreamingServiceCollectionExtensions.AddImplicitStreamSubscription(services,");
            sb.AppendLine($"            \"{ns}\", \"{grainTypeKey}\");");
        }

        if (persistentSlots.Count > 0) sb.AppendLine();
        foreach (PersistentStateSlot slot in persistentSlots)
        {
            string storageResolution = slot.ProviderName == DefaultStorageName
                ? "sp.GetRequiredService<global::Quark.Persistence.Abstractions.IGrainStorage>()"
                : $"sp.GetRequiredKeyedService<global::Quark.Persistence.Abstractions.IGrainStorage>(\"{slot.ProviderName}\")";

            sb.AppendLine($"        services.AddScoped<global::Quark.Persistence.Abstractions.IPersistentState<{slot.TArgFqn}>>(static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.PersistentState<{slot.TArgFqn}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>().Shell.GrainId,");
            sb.AppendLine($"                \"{slot.StateName}\",");
            sb.AppendLine($"                {storageResolution}));");
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        ctx.AddSource("QuarkRegistrations.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string SanitizeAssemblyName(string assemblyName)
    {
        var sb = new StringBuilder("Add");
        bool capitalizeNext = true;
        foreach (char c in assemblyName)
        {
            if (c == '.' || c == '-' || c == '_')
            {
                capitalizeNext = true;
                continue;
            }
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
        }
        sb.Append("Behaviors");
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Model
    // -----------------------------------------------------------------------

    private readonly struct PersistentStateSlot : IEquatable<PersistentStateSlot>
    {
        public PersistentStateSlot(string tArgFqn, string stateName, string providerName)
        {
            TArgFqn = tArgFqn;
            StateName = stateName;
            ProviderName = providerName;
        }

        public string TArgFqn { get; }
        public string StateName { get; }
        public string ProviderName { get; }

        public bool Equals(PersistentStateSlot other) =>
            TArgFqn == other.TArgFqn && StateName == other.StateName && ProviderName == other.ProviderName;

        public override bool Equals(object? obj) => obj is PersistentStateSlot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TArgFqn.GetHashCode();
                hash = hash * 397 ^ StateName.GetHashCode();
                hash = hash * 397 ^ ProviderName.GetHashCode();
                return hash;
            }
        }
    }

    private sealed class BehaviorModel
    {
        // Error-only model (QRK0050): only diagnostics are populated.
        public BehaviorModel(ImmutableArray<Diagnostic> diagnostics)
        {
            Diagnostics = diagnostics;
            BehaviorFqn = string.Empty;
            GrainInterfaceFqn = string.Empty;
            GrainTypeName = string.Empty;
            ProxyFqn = string.Empty;
            PlacementStrategyExpression = string.Empty;
            FactoryExpression = null;
            InMemoryStateTypes = ImmutableArray<string>.Empty;
            PersistentStateTypes = ImmutableArray<string>.Empty;
            ManagedStateTypes = ImmutableArray<string>.Empty;
            EagerStateTypes = ImmutableArray<string>.Empty;
            ImplicitStreamNamespaces = ImmutableArray<string>.Empty;
            PersistentStateSlots = ImmutableArray<PersistentStateSlot>.Empty;
            ImplementsUserServiceProviderFactory = false;
        }

        public BehaviorModel(
            string behaviorFqn,
            string grainInterfaceFqn,
            string grainTypeName,
            string proxyFqn,
            string placementStrategyExpression,
            string? factoryExpression,
            ImmutableArray<string> inMemoryStateTypes,
            ImmutableArray<string> persistentStateTypes,
            ImmutableArray<string> managedStateTypes,
            ImmutableArray<string> eagerStateTypes,
            ImmutableArray<string> implicitStreamNamespaces,
            ImmutableArray<PersistentStateSlot> persistentStateSlots,
            bool implementsUserServiceProviderFactory,
            ImmutableArray<Diagnostic> diagnostics)
        {
            BehaviorFqn = behaviorFqn;
            GrainInterfaceFqn = grainInterfaceFqn;
            GrainTypeName = grainTypeName;
            ProxyFqn = proxyFqn;
            PlacementStrategyExpression = placementStrategyExpression;
            FactoryExpression = factoryExpression;
            InMemoryStateTypes = inMemoryStateTypes;
            PersistentStateTypes = persistentStateTypes;
            ManagedStateTypes = managedStateTypes;
            EagerStateTypes = eagerStateTypes;
            ImplicitStreamNamespaces = implicitStreamNamespaces;
            PersistentStateSlots = persistentStateSlots;
            ImplementsUserServiceProviderFactory = implementsUserServiceProviderFactory;
            Diagnostics = diagnostics;
        }

        public bool IsValid => !string.IsNullOrEmpty(BehaviorFqn);
        public string BehaviorFqn { get; }
        public string GrainInterfaceFqn { get; }
        public string GrainTypeName { get; }
        public string ProxyFqn { get; }
        public string PlacementStrategyExpression { get; }
        public string? FactoryExpression { get; }
        public ImmutableArray<string> InMemoryStateTypes { get; }
        public ImmutableArray<string> PersistentStateTypes { get; }
        public ImmutableArray<string> ManagedStateTypes { get; }
        public ImmutableArray<string> EagerStateTypes { get; }
        public ImmutableArray<string> ImplicitStreamNamespaces { get; }
        public ImmutableArray<PersistentStateSlot> PersistentStateSlots { get; }
        public bool ImplementsUserServiceProviderFactory { get; }
        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
