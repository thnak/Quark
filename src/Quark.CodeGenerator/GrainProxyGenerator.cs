using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace Quark.CodeGenerator;

/// <summary>
/// Generates an AOT-safe grain proxy class for every interface that extends <c>IGrain</c>.
/// The proxy routes each method through <c>IGrainCallInvoker</c> so no runtime code generation
/// or reflection is required at call time.
///
/// Generated class naming: <c>{InterfaceName}Proxy</c> (strips leading 'I' if present).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GrainProxyGenerator : IIncrementalGenerator
{
    private const string IGrainFqn = "Quark.Core.Abstractions.IGrain";
    private const string IGrainCallInvokerFqn = "Quark.Core.Abstractions.IGrainCallInvoker";

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<InterfaceModel?> models = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: ExtractModel)
            .Where(static m => m is not null);

        context.RegisterSourceOutput(
            models.Collect(),
            static (ctx, items) => Emit(ctx, items!));
    }

    // -----------------------------------------------------------------------
    // Model extraction
    // -----------------------------------------------------------------------

    private static InterfaceModel? ExtractModel(
        GeneratorSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.Node is not InterfaceDeclarationSyntax)
            return null;
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol iface)
            return null;

        // Check that IGrain is in the interface hierarchy.
        bool isGrain = false;
        foreach (INamedTypeSymbol parent in iface.AllInterfaces)
        {
            if (parent.ToDisplayString() == IGrainFqn)
            {
                isGrain = true;
                break;
            }
        }
        if (!isGrain) return null;

        var ns = iface.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : iface.ContainingNamespace.ToDisplayString();

        // Collect all methods from the interface (excluding inherited grain marker methods).
        var methods = new List<MethodModel>();
        uint methodId = 0;
        foreach (ISymbol member in iface.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method)
                continue;

            // Determine return style: Task, Task<T>, ValueTask, ValueTask<T>
            string retType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            bool isTask = false;
            bool isTaskOfT = false;
            bool isValueTask = false;
            bool isValueTaskOfT = false;
            string taskResultType = "void";

            string retName = method.ReturnType.ToDisplayString();
            if (retName == "System.Threading.Tasks.Task")
                isTask = true;
            else if (method.ReturnType is INamedTypeSymbol nts &&
                     nts.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<TResult>")
            {
                isTaskOfT = true;
                taskResultType = nts.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else if (retName == "System.Threading.Tasks.ValueTask")
                isValueTask = true;
            else if (method.ReturnType is INamedTypeSymbol nvts &&
                     nvts.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.ValueTask<TResult>")
            {
                isValueTaskOfT = true;
                taskResultType = nvts.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            var parameters = method.Parameters
                .Select(p => new ParameterModel(
                    p.Name,
                    p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .ToList();

            methods.Add(new MethodModel(
                methodId++,
                method.Name,
                retType,
                isTask, isTaskOfT, isValueTask, isValueTaskOfT, taskResultType,
                parameters));
        }

        if (methods.Count == 0) return null;

        string proxySuffix = iface.Name.StartsWith("I") && iface.Name.Length > 1
            ? iface.Name.Substring(1) + "Proxy"
            : iface.Name + "Proxy";

        return new InterfaceModel(
            ns,
            iface.Name,
            iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            proxySuffix,
            methods);
    }

    // -----------------------------------------------------------------------
    // Code emission
    // -----------------------------------------------------------------------

    private static void Emit(
        SourceProductionContext ctx,
        ImmutableArray<InterfaceModel> models)
    {
        foreach (InterfaceModel model in models)
        {
            string source = BuildProxySource(model);
            ctx.AddSource(
                $"{model.ProxyClassName}.QuarkProxy.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string BuildProxySource(InterfaceModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by Quark.CodeGenerator — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();

        bool hasNs = !string.IsNullOrEmpty(m.Namespace);
        if (hasNs)
        {
            sb.AppendLine($"namespace {m.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Quark.CodeGenerator\", \"0.1.0\")]");
        sb.AppendLine($"internal sealed class {m.ProxyClassName} : {m.FqInterfaceName}");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::Quark.Core.Abstractions.GrainId _grainId;");
        sb.AppendLine("    private readonly global::Quark.Core.Abstractions.IGrainCallInvoker _invoker;");
        sb.AppendLine();
        sb.AppendLine($"    public {m.ProxyClassName}(");
        sb.AppendLine("        global::Quark.Core.Abstractions.GrainId grainId,");
        sb.AppendLine("        global::Quark.Core.Abstractions.IGrainCallInvoker invoker)");
        sb.AppendLine("    {");
        sb.AppendLine("        _grainId = grainId;");
        sb.AppendLine("        _invoker = invoker;");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (MethodModel method in m.Methods)
        {
            EmitMethod(sb, method);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitMethod(StringBuilder sb, MethodModel method)
    {
        // Build parameter list string
        var paramList = string.Join(", ", method.Parameters.Select(p => $"{p.FqTypeName} {p.Name}"));
        // Build args array
        string argsArray = method.Parameters.Count == 0
            ? "null"
            : $"new object?[] {{ {string.Join(", ", method.Parameters.Select(p => p.Name))} }}";

        sb.Append($"    public {method.ReturnType} {method.Name}(");
        sb.Append(paramList);
        sb.AppendLine(")");
        sb.AppendLine("    {");

        if (method.IsTask)
        {
            sb.AppendLine($"        return _invoker.InvokeVoidAsync(_grainId, {method.MethodId}u, {argsArray});");
        }
        else if (method.IsTaskOfT)
        {
            sb.AppendLine($"        return _invoker.InvokeAsync<{method.TaskResultType}>(_grainId, {method.MethodId}u, {argsArray});");
        }
        else if (method.IsValueTask)
        {
            sb.AppendLine($"        return new global::System.Threading.Tasks.ValueTask(_invoker.InvokeVoidAsync(_grainId, {method.MethodId}u, {argsArray}));");
        }
        else if (method.IsValueTaskOfT)
        {
            sb.AppendLine($"        return new global::System.Threading.Tasks.ValueTask<{method.TaskResultType}>(_invoker.InvokeAsync<{method.TaskResultType}>(_grainId, {method.MethodId}u, {argsArray}));");
        }
        else
        {
            // Unknown return type — emit a throw for now (analyzer will flag this).
            sb.AppendLine($"        throw new global::System.NotSupportedException(\"Grain methods must return Task or Task<T>.\");");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    // -----------------------------------------------------------------------
    // Data models
    // -----------------------------------------------------------------------

    private sealed class InterfaceModel(
        string @namespace,
        string interfaceName,
        string fqInterfaceName,
        string proxyClassName,
        IReadOnlyList<MethodModel> methods)
    {
        public string Namespace { get; } = @namespace;
        public string InterfaceName { get; } = interfaceName;
        public string FqInterfaceName { get; } = fqInterfaceName;
        public string ProxyClassName { get; } = proxyClassName;
        public IReadOnlyList<MethodModel> Methods { get; } = methods;
    }

    private sealed class MethodModel(
        uint methodId,
        string name,
        string returnType,
        bool isTask,
        bool isTaskOfT,
        bool isValueTask,
        bool isValueTaskOfT,
        string taskResultType,
        IReadOnlyList<ParameterModel> parameters)
    {
        public uint MethodId { get; } = methodId;
        public string Name { get; } = name;
        public string ReturnType { get; } = returnType;
        public bool IsTask { get; } = isTask;
        public bool IsTaskOfT { get; } = isTaskOfT;
        public bool IsValueTask { get; } = isValueTask;
        public bool IsValueTaskOfT { get; } = isValueTaskOfT;
        public string TaskResultType { get; } = taskResultType;
        public IReadOnlyList<ParameterModel> Parameters { get; } = parameters;
    }

    private sealed class ParameterModel(string name, string fqTypeName)
    {
        public string Name { get; } = name;
        public string FqTypeName { get; } = fqTypeName;
    }
}

