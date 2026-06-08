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
///     </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class BehaviorRegistrationGenerator : IIncrementalGenerator
{
    private const string IGrainBehaviorFqn = "Quark.Core.Abstractions.Grains.IGrainBehavior";
    private const string IGrainFqn = "Quark.Core.Abstractions.Grains.IGrain";
    private const string GrainBehaviorAttributeFqn = "Quark.Core.Abstractions.Grains.GrainBehaviorAttribute";
    private const string IActivationMemoryNs = "Quark.Core.Abstractions.Hosting";
    private const string IActivationMemoryName = "IActivationMemory";
    private const string IPersistentActivationMemoryNs = "Quark.Persistence.Abstractions";
    private const string IPersistentActivationMemoryName = "IPersistentActivationMemory";
    private const string IManagedActivationMemoryNs = "Quark.Core.Abstractions.Hosting";
    private const string IManagedActivationMemoryName = "IManagedActivationMemory";

    internal static readonly DiagnosticDescriptor MissingGrainInterface = new(
        id: "QRK0020",
        title: "Behavior missing grain interface",
        messageFormat: "'{0}' implements IGrainBehavior but no IGrain-derived interface. Add [GrainBehavior(\"typeName\")] or implement the grain interface.",
        category: "Quark.CodeGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AmbiguousGrainInterface = new(
        id: "QRK0021",
        title: "Behavior implements multiple grain interfaces",
        messageFormat: "'{0}' implements multiple IGrain-derived interfaces. The first ('{1}') is used. Add a single grain interface to silence.",
        category: "Quark.CodeGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
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

        ImmutableArray<Diagnostic> diagnostics = ImmutableArray<Diagnostic>.Empty;
        if (grainIfaces.Count > 1)
        {
            Location location = type.Locations.FirstOrDefault() ?? Location.None;
            diagnostics = ImmutableArray.Create(
                Diagnostic.Create(AmbiguousGrainInterface, location, type.Name, grainIface.Name));
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

        string behaviorNs = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();
        string behaviorFqn = string.IsNullOrEmpty(behaviorNs)
            ? $"global::{type.Name}"
            : $"global::{behaviorNs}.{type.Name}";

        var inMemory = new List<string>();
        var persistent = new List<string>();
        var managed = new List<string>();

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
            }
        }

        return new BehaviorModel(
            behaviorFqn: behaviorFqn,
            grainInterfaceFqn: grainIfaceFqn,
            grainTypeName: grainTypeName,
            proxyFqn: proxyFqn,
            inMemoryStateTypes: inMemory.Distinct().ToImmutableArray(),
            persistentStateTypes: persistent.Distinct().ToImmutableArray(),
            managedStateTypes: managed.Distinct().ToImmutableArray(),
            diagnostics: diagnostics);
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
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddGrainBehavior<{m.GrainInterfaceFqn}, {m.BehaviorFqn}>(services);");
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddGrainTransportDispatcher(services,");
            sb.AppendLine($"            new global::Quark.Core.Abstractions.Identity.GrainType(\"{m.GrainTypeName}\"),");
            sb.AppendLine($"            {m.ProxyFqn}_TransportDispatcher.Instance);");
        }

        if (inMemoryStates.Count > 0) sb.AppendLine();
        foreach (string tArg in inMemoryStates)
        {
            sb.AppendLine($"        services.AddScoped<global::Quark.Core.Abstractions.Hosting.IActivationMemory<{tArg}>>(static sp =>");
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
            sb.AppendLine($"        services.AddScoped<global::Quark.Core.Abstractions.Hosting.IManagedActivationMemory<{tArg}>>(static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.ManagedActivationMemoryAccessor<{tArg}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>()");
            sb.AppendLine($"                  .Shell.GetOrCreateManagedHolder<{tArg}>()));");
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

    private sealed class BehaviorModel
    {
        // Error-only model (QRK0010): only diagnostics are populated.
        public BehaviorModel(ImmutableArray<Diagnostic> diagnostics)
        {
            Diagnostics = diagnostics;
            BehaviorFqn = string.Empty;
            GrainInterfaceFqn = string.Empty;
            GrainTypeName = string.Empty;
            ProxyFqn = string.Empty;
            InMemoryStateTypes = ImmutableArray<string>.Empty;
            PersistentStateTypes = ImmutableArray<string>.Empty;
            ManagedStateTypes = ImmutableArray<string>.Empty;
        }

        public BehaviorModel(
            string behaviorFqn,
            string grainInterfaceFqn,
            string grainTypeName,
            string proxyFqn,
            ImmutableArray<string> inMemoryStateTypes,
            ImmutableArray<string> persistentStateTypes,
            ImmutableArray<string> managedStateTypes,
            ImmutableArray<Diagnostic> diagnostics)
        {
            BehaviorFqn = behaviorFqn;
            GrainInterfaceFqn = grainInterfaceFqn;
            GrainTypeName = grainTypeName;
            ProxyFqn = proxyFqn;
            InMemoryStateTypes = inMemoryStateTypes;
            PersistentStateTypes = persistentStateTypes;
            ManagedStateTypes = managedStateTypes;
            Diagnostics = diagnostics;
        }

        public bool IsValid => !string.IsNullOrEmpty(BehaviorFqn);
        public string BehaviorFqn { get; }
        public string GrainInterfaceFqn { get; }
        public string GrainTypeName { get; }
        public string ProxyFqn { get; }
        public ImmutableArray<string> InMemoryStateTypes { get; }
        public ImmutableArray<string> PersistentStateTypes { get; }
        public ImmutableArray<string> ManagedStateTypes { get; }
        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
