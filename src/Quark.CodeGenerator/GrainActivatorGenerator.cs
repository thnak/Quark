using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Quark.CodeGenerator;

/// <summary>
///     Generates AOT-safe grain activator factories for concrete grain classes.
///     Each generated factory constructs the grain directly and resolves constructor
///     dependencies from <see cref="System.IServiceProvider" />, mirroring Orleans-style DI activation.
///     Constructor parameters annotated with <c>[PersistentState]</c> are wired to
///     <c>PersistentState&lt;T&gt;</c> instances instead of plain DI resolution.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GrainActivatorGenerator : IIncrementalGenerator
{
    private const string GrainFqn = "Quark.Core.Abstractions.Grains.Grain";
    private const string PersistentStateAttributeFqn = "Quark.Persistence.Abstractions.PersistentStateAttribute";
    private const string DefaultProviderName = "Default";

    private const string ActivatorUtilitiesConstructorFqn =
        "Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GrainModel?> models = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                ExtractModel)
            .Where(static model => model is not null);

        context.RegisterSourceOutput(
            models.Collect(),
            static (ctx, items) => Emit(ctx, items!));
    }

    private static GrainModel? ExtractModel(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(declaration, ct) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        if (typeSymbol.IsAbstract || typeSymbol.IsGenericType)
        {
            return null;
        }

        bool isGrain = false;
        for (INamedTypeSymbol? current = typeSymbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == GrainFqn)
            {
                isGrain = true;
                break;
            }
        }

        if (!isGrain)
        {
            return null;
        }

        IMethodSymbol? ctor = SelectConstructor(typeSymbol);
        if (ctor is null)
        {
            return null;
        }

        string ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var parameters = ctor.Parameters
            .Select(p => BuildParameterModel(p))
            .ToList();

        return new GrainModel(
            ns,
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            typeSymbol.Name + "ActivatorFactory",
            parameters);
    }

    private static ParameterModel BuildParameterModel(IParameterSymbol p)
    {
        string fqTypeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        foreach (AttributeData attr in p.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != PersistentStateAttributeFqn)
            {
                continue;
            }

            if (attr.ConstructorArguments.Length == 0)
            {
                break;
            }

            string stateName = attr.ConstructorArguments[0].Value as string ?? string.Empty;
            string providerName = attr.ConstructorArguments.Length > 1
                ? attr.ConstructorArguments[1].Value as string ?? DefaultProviderName
                : DefaultProviderName;

            // Extract TState from IPersistentState<TState>
            string stateFqTypeName = p.Type is INamedTypeSymbol nts && nts.TypeArguments.Length == 1
                ? nts.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : "object";

            return new ParameterModel(p.Name, fqTypeName,
                new PersistentStateInfo(stateName, providerName, stateFqTypeName));
        }

        return new ParameterModel(p.Name, fqTypeName, null);
    }

    private static IMethodSymbol? SelectConstructor(INamedTypeSymbol typeSymbol)
    {
        var publicConstructors = typeSymbol.InstanceConstructors
            .Where(static ctor => ctor.MethodKind == MethodKind.Constructor &&
                                  ctor.DeclaredAccessibility == Accessibility.Public)
            .ToImmutableArray();

        if (publicConstructors.Length == 0)
        {
            return null;
        }

        foreach (IMethodSymbol ctor in publicConstructors)
        {
            if (ctor.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ActivatorUtilitiesConstructorFqn))
            {
                return ctor;
            }
        }

        return publicConstructors
            .OrderByDescending(static ctor => ctor.Parameters.Length)
            .First();
    }

    private static void Emit(SourceProductionContext ctx, ImmutableArray<GrainModel> models)
    {
        foreach (GrainModel model in models)
        {
            string source = BuildSource(model);
            ctx.AddSource(
                $"{model.FactoryClassName}.QuarkActivator.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string BuildSource(GrainModel model)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by Quark.CodeGenerator — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Quark.CodeGenerator\", \"0.1.0\")]");
        sb.AppendLine($"internal sealed class {model.FactoryClassName} : global::Quark.Runtime.IGrainActivatorFactory");
        sb.AppendLine("{");
        sb.AppendLine($"    public global::System.Type GrainClass => typeof({model.FqTypeName});");
        sb.AppendLine();
        sb.AppendLine(
            "    public global::Quark.Core.Abstractions.Grains.Grain Create(global::Quark.Core.Abstractions.Identity.GrainId grainId, global::System.IServiceProvider services)");
        sb.AppendLine("    {");

        if (model.Parameters.Count == 0)
        {
            sb.AppendLine($"        return new {model.FqTypeName}();");
        }
        else
        {
            sb.AppendLine($"        return new {model.FqTypeName}(");
            for (int i = 0; i < model.Parameters.Count; i++)
            {
                ParameterModel parameter = model.Parameters[i];
                string suffix = i < model.Parameters.Count - 1 ? "," : string.Empty;

                if (parameter.PersistentState is { } ps)
                {
                    string storageExpr = ps.ProviderName == DefaultProviderName
                        ? "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Quark.Persistence.Abstractions.IGrainStorage>(services)"
                        : $"global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<global::Quark.Persistence.Abstractions.IGrainStorage>(services, \"{ps.ProviderName}\")";

                    sb.AppendLine(
                        $"            new global::Quark.Persistence.Abstractions.PersistentState<{ps.StateFqTypeName}>(grainId, \"{ps.StateName}\", {storageExpr}){suffix}");
                }
                else
                {
                    sb.AppendLine(
                        $"            global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{parameter.FqTypeName}>(services){suffix}");
                }
            }

            sb.AppendLine("        );");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private sealed class GrainModel(
        string @namespace,
        string fqTypeName,
        string factoryClassName,
        IReadOnlyList<ParameterModel> parameters)
    {
        public string Namespace { get; } = @namespace;
        public string FqTypeName { get; } = fqTypeName;
        public string FactoryClassName { get; } = factoryClassName;
        public IReadOnlyList<ParameterModel> Parameters { get; } = parameters;
    }

    private sealed class ParameterModel(string name, string fqTypeName, PersistentStateInfo? persistentState)
    {
        public string Name { get; } = name;
        public string FqTypeName { get; } = fqTypeName;
        public PersistentStateInfo? PersistentState { get; } = persistentState;
    }

    private sealed class PersistentStateInfo(string stateName, string providerName, string stateFqTypeName)
    {
        public string StateName { get; } = stateName;
        public string ProviderName { get; } = providerName;
        public string StateFqTypeName { get; } = stateFqTypeName;
    }
}
