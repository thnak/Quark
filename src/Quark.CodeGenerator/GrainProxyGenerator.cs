using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Quark.CodeGenerator;

/// <summary>
///     Generates an AOT-safe grain proxy class for every interface that extends <c>IGrain</c>.
///     The proxy routes each method through <c>IGrainCallInvoker</c> so no runtime code generation
///     or reflection is required at call time.
///     Generated class naming: <c>{InterfaceName}Proxy</c> (strips leading 'I' if present).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GrainProxyGenerator : IIncrementalGenerator
{
    private const string IGrainFqn = "Quark.Core.Abstractions.Grains.IGrain";
    private const string IGrainObserverFqn = "Quark.Core.Abstractions.Grains.IGrainObserver";
    private const string IGrainCallInvokerFqn = "Quark.Core.Abstractions.Hosting.IGrainCallInvoker";
    private const string IGrainProxyActivatorFqn = "Quark.Core.Abstractions.Hosting.IGrainProxyActivator";
    private const string IGrainObserverProxyActivatorFqn = "Quark.Core.Abstractions.Hosting.IGrainObserverProxyActivator";
    private const string ImmutableAttributeFqn = "Quark.Core.Abstractions.ImmutableAttribute";
    private const string GenerateSerializerFqn = "Quark.Serialization.Abstractions.Attributes.GenerateSerializerAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<InterfaceModel?> models = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InterfaceDeclarationSyntax,
                ExtractModel)
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
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol iface)
        {
            return null;
        }

        // Check that IGrain or IGrainObserver is in the interface hierarchy.
        bool isGrain = false;
        bool isObserver = false;
        foreach (INamedTypeSymbol parent in iface.AllInterfaces)
        {
            string fqn = parent.ToDisplayString();
            if (fqn == IGrainFqn) isGrain = true;
            else if (fqn == IGrainObserverFqn) isObserver = true;
        }

        // Only generate for concrete grain or observer subtypes, never for types that extend both
        // (ambiguous proxy activator) or for pure marker types with no methods.
        if (!isGrain && !isObserver)
        {
            return null;
        }

        if (isGrain && isObserver)
        {
            return null; // ambiguous — skip silently
        }

        string ns = iface.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : iface.ContainingNamespace.ToDisplayString();

        // Collect all methods from the interface (excluding inherited grain marker methods).
        var methods = new List<MethodModel>();
        uint methodId = 0;
        foreach (ISymbol member in iface.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            // Determine return style: Task, Task<T>, ValueTask, ValueTask<T>
            string retType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            bool isTask = false;
            bool isTaskOfT = false;
            bool isValueTask = false;
            bool isValueTaskOfT = false;
            string taskResultType = "void";

            string retName = method.ReturnType.ToDisplayString();
            if (retName == "System.Threading.Tasks.Task")
            {
                isTask = true;
            }
            else if (method.ReturnType is INamedTypeSymbol nts &&
                     nts.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<TResult>")
            {
                isTaskOfT = true;
                taskResultType = nts.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else if (retName == "System.Threading.Tasks.ValueTask")
            {
                isValueTask = true;
            }
            else if (method.ReturnType is INamedTypeSymbol nvts &&
                     nvts.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.ValueTask<TResult>")
            {
                isValueTaskOfT = true;
                taskResultType = nvts.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            var parameters = method.Parameters
                .Select(p => BuildParameterModel(p))
                .ToList();

            methods.Add(new MethodModel(
                methodId++,
                method.Name,
                retType,
                isTask, isTaskOfT, isValueTask, isValueTaskOfT, taskResultType,
                parameters));
        }

        if (methods.Count == 0)
        {
            return null;
        }

        string proxySuffix = iface.Name.StartsWith("I") && iface.Name.Length > 1
            ? iface.Name.Substring(1) + "Proxy"
            : iface.Name + "Proxy";

        return new InterfaceModel(
            ns,
            iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            proxySuffix,
            methods,
            isObserver);
    }

    // -----------------------------------------------------------------------
    // Parameter clone strategy
    // -----------------------------------------------------------------------

    private static ParameterModel BuildParameterModel(IParameterSymbol param)
    {
        ITypeSymbol type = param.Type;
        string name = param.Name;
        string fqTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Value types (struct, enum, primitive) are copied by value — no defensive clone needed.
        if (type.IsValueType)
            return new ParameterModel(name, fqTypeName, CloneKind.None);

        // string is immutable — safe to share.
        if (type.SpecialType == SpecialType.System_String)
            return new ParameterModel(name, fqTypeName, CloneKind.None);

        // [Immutable]-annotated types are opt-in safe-to-share.
        if (HasAttribute(type, ImmutableAttributeFqn))
            return new ParameterModel(name, fqTypeName, CloneKind.None);

        // IReadOnly* interfaces guarantee the caller can't mutate through the reference.
        if (type is INamedTypeSymbol readOnlyNamed && readOnlyNamed.IsGenericType)
        {
            string def = readOnlyNamed.ConstructedFrom.ToDisplayString();
            if (def is "System.Collections.Generic.IReadOnlyList<T>"
                    or "System.Collections.Generic.IReadOnlyCollection<T>"
                    or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                    or "System.Collections.Generic.IReadOnlySet<T>")
                return new ParameterModel(name, fqTypeName, CloneKind.None);
        }

        // Mutable generic collections — copy the container, share element references.
        if (type is INamedTypeSymbol genericNamed && genericNamed.IsGenericType)
        {
            string def = genericNamed.ConstructedFrom.ToDisplayString();
            if (def is "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>")
            {
                string elemFq = genericNamed.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return new ParameterModel(name, fqTypeName, CloneKind.NewList, elemFq);
            }

            if (def == "System.Collections.Generic.Dictionary<TKey, TValue>")
            {
                string keyFq = genericNamed.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string valFq = genericNamed.TypeArguments[1]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return new ParameterModel(name, fqTypeName, CloneKind.NewDictionary, keyFq, valFq);
            }
        }

        // Arrays — copy the container, share element references.
        if (type is IArrayTypeSymbol arrayType)
        {
            string elemFq = arrayType.ElementType
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new ParameterModel(name, fqTypeName, CloneKind.NewArray, elemFq);
        }

        // [GenerateSerializer] types have a generated {TypeName}Copier.CloneStatic() method.
        if (type is INamedTypeSymbol gsNamed && HasAttribute(type, GenerateSerializerFqn))
        {
            string ns = gsNamed.ContainingNamespace.IsGlobalNamespace
                ? "global::"
                : $"global::{gsNamed.ContainingNamespace.ToDisplayString()}.";
            string copierFq = $"{ns}{gsNamed.Name}Copier";
            return new ParameterModel(name, fqTypeName, CloneKind.GeneratedCopier,
                copierFqTypeName: copierFq);
        }

        // Unknown mutable reference type — no clone at invokable level.
        return new ParameterModel(name, fqTypeName, CloneKind.None);
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

        // Emit one invokable struct per method before the proxy class.
        foreach (MethodModel method in m.Methods)
        {
            EmitInvokableStruct(sb, m, method);
        }

        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Quark.CodeGenerator\", \"0.1.0\")]");
        string activatorInterface = m.IsObserver
            ? $"global::Quark.Core.Abstractions.Hosting.IGrainObserverProxyActivator<{m.ProxyClassName}>"
            : $"global::Quark.Core.Abstractions.Hosting.IGrainProxyActivator<{m.ProxyClassName}>";

        sb.AppendLine($"internal sealed class {m.ProxyClassName}");
        sb.AppendLine($"    : {m.FqInterfaceName}");
        sb.AppendLine($"    , {activatorInterface}");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::Quark.Core.Abstractions.Identity.GrainId _grainId;");
        sb.AppendLine("    private readonly global::Quark.Core.Abstractions.Hosting.IGrainCallInvoker _invoker;");
        sb.AppendLine();
        sb.AppendLine($"    public {m.ProxyClassName}(");
        sb.AppendLine("        global::Quark.Core.Abstractions.Identity.GrainId grainId,");
        sb.AppendLine("        global::Quark.Core.Abstractions.Hosting.IGrainCallInvoker invoker)");
        sb.AppendLine("    {");
        sb.AppendLine("        _grainId = grainId;");
        sb.AppendLine("        _invoker = invoker;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public static {m.ProxyClassName} Create(");
        sb.AppendLine("        global::Quark.Core.Abstractions.Identity.GrainId grainId,");
        sb.AppendLine("        global::Quark.Core.Abstractions.Hosting.IGrainCallInvoker invoker)");
        sb.AppendLine($"        => new {m.ProxyClassName}(grainId, invoker);");
        sb.AppendLine();

        foreach (MethodModel method in m.Methods)
        {
            EmitMethod(sb, m, method);
        }

        sb.AppendLine("}");
        sb.AppendLine();

        // Emit one transport dispatcher per interface (used by MessageDispatcher for TCP path).
        EmitTransportDispatcher(sb, m);

        return sb.ToString();
    }

    private static void EmitInvokableStruct(StringBuilder sb, InterfaceModel m, MethodModel method)
    {
        bool isVoid = method.IsTask || method.IsValueTask;
        string structName = $"{m.ProxyClassName}_{method.Name}Invokable";

        string invokableInterface;
        if (m.IsObserver)
        {
            invokableInterface = "global::Quark.Core.Abstractions.Hosting.IObserverVoidInvokable";
        }
        else if (isVoid)
        {
            invokableInterface = "global::Quark.Core.Abstractions.Hosting.IGrainVoidInvokable";
        }
        else
        {
            invokableInterface = $"global::Quark.Core.Abstractions.Hosting.IGrainInvokable<{method.TaskResultType}>";
        }

        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Quark.CodeGenerator\", \"0.1.0\")]");
        sb.AppendLine($"internal readonly struct {structName}");
        sb.AppendLine($"    : {invokableInterface}");
        sb.AppendLine("{");

        // One backing field per parameter.
        foreach (ParameterModel p in method.Parameters)
        {
            sb.AppendLine($"    private readonly {p.FqTypeName} _{p.Name};");
        }

        // Constructor (only when there are parameters).
        if (method.Parameters.Count > 0)
        {
            string ctorParams = string.Join(", ", method.Parameters.Select(p => $"{p.FqTypeName} {p.Name}"));
            sb.AppendLine();
            sb.AppendLine($"    public {structName}({ctorParams})");
            sb.AppendLine("    {");
            foreach (ParameterModel p in method.Parameters)
            {
                sb.AppendLine($"        _{p.Name} = {p.Name};");
            }
            sb.AppendLine("    }");
        }

        // MethodId property for tracing and fault injection.
        sb.AppendLine();
        sb.AppendLine($"    public uint MethodId => {method.MethodId}u;");

        string argList = string.Join(", ", method.Parameters.Select(p => $"_{p.Name}"));

        sb.AppendLine();
        if (m.IsObserver)
        {
            // Observer: cast target to the observer interface (not Grain).
            // All observer methods are treated as void (fire-and-forget).
            string callExpr = $"(({m.FqInterfaceName})target).{method.Name}({argList})";
            string invokeBody = method.IsTask
                ? $"        => new global::System.Threading.Tasks.ValueTask({callExpr});"
                : $"        => {callExpr};";
            sb.AppendLine("    public global::System.Threading.Tasks.ValueTask Invoke(object target)");
            sb.AppendLine(invokeBody);
        }
        else if (isVoid)
        {
            // Grain void: cast to the grain interface.
            string callExpr = $"(({m.FqInterfaceName})grain).{method.Name}({argList})";
            // Task → wrap in ValueTask; ValueTask → return directly (ValueTask ctor takes Task, not ValueTask)
            string invokeBody = method.IsTask
                ? $"        => new global::System.Threading.Tasks.ValueTask({callExpr});"
                : $"        => {callExpr};";
            sb.AppendLine("    public global::System.Threading.Tasks.ValueTask Invoke(");
            sb.AppendLine("        global::Quark.Core.Abstractions.Grains.Grain grain)");
            sb.AppendLine(invokeBody);
        }
        else
        {
            // Grain returning value: cast to the grain interface.
            string callExpr = $"(({m.FqInterfaceName})grain).{method.Name}({argList})";
            // Task<T> → wrap in ValueTask<T>; ValueTask<T> → return directly
            string invokeBody = method.IsTaskOfT
                ? $"        => new global::System.Threading.Tasks.ValueTask<{method.TaskResultType}>({callExpr});"
                : $"        => {callExpr};";
            sb.AppendLine($"    public global::System.Threading.Tasks.ValueTask<{method.TaskResultType}> Invoke(");
            sb.AppendLine("        global::Quark.Core.Abstractions.Grains.Grain grain)");
            sb.AppendLine(invokeBody);
        }

        // Clone() — creates a data-isolated copy of this invokable's arguments.
        // For value types and immutable reference types this is a no-op struct copy.
        // For mutable collections (List<T>, T[], Dictionary<K,V>) a new container is allocated;
        // element references are shared (shallow container copy).
        EmitCloneMethod(sb, structName, method);

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitCloneMethod(StringBuilder sb, string structName, MethodModel method)
    {
        bool needsClone = method.Parameters.Any(p => p.CloneKind != CloneKind.None);

        sb.AppendLine();
        sb.AppendLine($"    public {structName} Clone()");
        sb.AppendLine("    {");

        if (!needsClone)
        {
            sb.AppendLine("        return this;");
        }
        else
        {
            var ctorArgExprs = new List<string>();
            foreach (ParameterModel p in method.Parameters)
            {
                string expr = p.CloneKind switch
                {
                    CloneKind.NewList =>
                        $"_{p.Name} is null ? default! : new global::System.Collections.Generic.List<{p.ElementFqTypeName}>(_{p.Name})",
                    CloneKind.NewArray =>
                        $"_{p.Name}?.ToArray()!",
                    CloneKind.NewDictionary =>
                        $"_{p.Name} is null ? default! : new global::System.Collections.Generic.Dictionary<{p.ElementFqTypeName}, {p.ValueFqTypeName}>(_{p.Name})",
                    CloneKind.GeneratedCopier =>
                        $"_{p.Name} is null ? default! : {p.CopierFqTypeName}.CloneStatic(_{p.Name})",
                    _ => $"_{p.Name}"
                };
                ctorArgExprs.Add(expr);
            }
            sb.AppendLine($"        return new {structName}({string.Join(", ", ctorArgExprs)});");
        }

        sb.AppendLine("    }");
    }

    private static void EmitMethod(StringBuilder sb, InterfaceModel m, MethodModel method)
    {
        string paramList = string.Join(", ", method.Parameters.Select(p => $"{p.FqTypeName} {p.Name}"));
        string structName = $"{m.ProxyClassName}_{method.Name}Invokable";
        string ctorArgs = string.Join(", ", method.Parameters.Select(p => p.Name));
        string structNew = method.Parameters.Count == 0
            ? $"new {structName}()"
            : $"new {structName}({ctorArgs})";
        // Always call Clone() — for value-type-only invokables this is return this; (zero cost).
        string structCreate = $"{structNew}.Clone()";

        sb.Append($"    public {method.ReturnType} {method.Name}(");
        sb.Append(paramList);
        sb.AppendLine(")");
        sb.AppendLine("    {");

        if (m.IsObserver)
        {
            // Observer methods are always treated as void (fire-and-forget via InvokeObserverAsync).
            if (method.IsTask)
            {
                sb.AppendLine($"        return _invoker.InvokeObserverAsync(_grainId, {structCreate});");
            }
            else if (method.IsValueTask)
            {
                sb.AppendLine(
                    $"        return new global::System.Threading.Tasks.ValueTask(_invoker.InvokeObserverAsync(_grainId, {structCreate}));");
            }
            else
            {
                sb.AppendLine(
                    "        throw new global::System.NotSupportedException(\"Observer methods must return Task or ValueTask.\");");
            }
        }
        else if (method.IsTask)
        {
            sb.AppendLine($"        return _invoker.InvokeVoidAsync(_grainId, {structCreate});");
        }
        else if (method.IsTaskOfT)
        {
            sb.AppendLine(
                $"        return _invoker.InvokeAsync<{structName}, {method.TaskResultType}>(_grainId, {structCreate});");
        }
        else if (method.IsValueTask)
        {
            sb.AppendLine(
                $"        return new global::System.Threading.Tasks.ValueTask(_invoker.InvokeVoidAsync(_grainId, {structCreate}));");
        }
        else if (method.IsValueTaskOfT)
        {
            sb.AppendLine(
                $"        return new global::System.Threading.Tasks.ValueTask<{method.TaskResultType}>(_invoker.InvokeAsync<{structName}, {method.TaskResultType}>(_grainId, {structCreate}));");
        }
        else
        {
            // Unknown return type — emit a throw for now (analyzer will flag this).
            sb.AppendLine(
                "        throw new global::System.NotSupportedException(\"Grain methods must return Task or Task<T>.\");");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void EmitTransportDispatcher(StringBuilder sb, InterfaceModel m)
    {
        string dispatcherName = $"{m.ProxyClassName}_TransportDispatcher";

        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Quark.CodeGenerator\", \"0.1.0\")]");
        sb.AppendLine($"internal sealed class {dispatcherName}");
        sb.AppendLine("    : global::Quark.Core.Abstractions.Hosting.ITransportGrainDispatcher");
        sb.AppendLine("{");
        sb.AppendLine($"    public static readonly {dispatcherName} Instance = new();");
        sb.AppendLine();
        sb.AppendLine("    public async global::System.Threading.Tasks.Task<object?> DispatchAsync(");
        sb.AppendLine("        global::Quark.Core.Abstractions.Identity.GrainId grainId,");
        sb.AppendLine("        uint methodId,");
        sb.AppendLine("        global::System.ReadOnlyMemory<byte> argumentPayload,");
        sb.AppendLine("        global::Quark.Core.Abstractions.Hosting.IGrainCallInvoker invoker,");
        sb.AppendLine("        global::System.Threading.CancellationToken ct = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (methodId)");
        sb.AppendLine("        {");

        foreach (MethodModel method in m.Methods)
        {
            string structName = $"{m.ProxyClassName}_{method.Name}Invokable";

            sb.AppendLine($"            case {method.MethodId}u:");
            sb.AppendLine("            {");

            if (method.Parameters.Count > 0)
            {
                sb.AppendLine("                var _reader = new global::Quark.Serialization.Abstractions.Buffers.CodecReader(argumentPayload);");
                for (int i = 0; i < method.Parameters.Count; i++)
                {
                    string fqType = method.Parameters[i].FqTypeName;
                    sb.AppendLine($"                var _arg{i} = ({fqType})global::Quark.Runtime.GrainMessageSerializer.ReadArg(ref _reader)!;");
                }
            }

            string ctorArgs = method.Parameters.Count == 0
                ? string.Empty
                : string.Join(", ", Enumerable.Range(0, method.Parameters.Count).Select(i => $"_arg{i}"));
            string structCreate = method.Parameters.Count == 0
                ? $"new {structName}()"
                : $"new {structName}({ctorArgs})";

            if (m.IsObserver)
            {
                sb.AppendLine($"                await invoker.InvokeObserverAsync<{structName}>(grainId, {structCreate}, ct).ConfigureAwait(false);");
                sb.AppendLine("                return null;");
            }
            else if (method.IsTask || method.IsValueTask)
            {
                sb.AppendLine($"                await invoker.InvokeVoidAsync<{structName}>(grainId, {structCreate}, ct).ConfigureAwait(false);");
                sb.AppendLine("                return null;");
            }
            else
            {
                sb.AppendLine($"                return await invoker.InvokeAsync<{structName}, {method.TaskResultType}>(grainId, {structCreate}, ct).ConfigureAwait(false);");
            }

            sb.AppendLine("            }");
        }

        sb.AppendLine("        }");
        sb.AppendLine("        throw new global::System.InvalidOperationException(");
        sb.AppendLine($"            $\"Unknown method ID {{methodId}} for grain type {{grainId.Type.Value}}.\");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    // -----------------------------------------------------------------------
    // Data models
    // -----------------------------------------------------------------------

    private sealed class InterfaceModel(
        string @namespace,
        string fqInterfaceName,
        string proxyClassName,
        IReadOnlyList<MethodModel> methods,
        bool isObserver)
    {
        public string Namespace { get; } = @namespace;
        public string FqInterfaceName { get; } = fqInterfaceName;
        public string ProxyClassName { get; } = proxyClassName;
        public IReadOnlyList<MethodModel> Methods { get; } = methods;
        public bool IsObserver { get; } = isObserver;
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

    private enum CloneKind
    {
        None,            // value type, string, [Immutable], or readonly interface — no clone needed
        NewList,         // List<T> or IList<T> — new container, element refs shared
        NewArray,        // T[] — ToArray() clone
        NewDictionary,   // Dictionary<K,V> — new container, entry refs shared
        GeneratedCopier  // [GenerateSerializer] — call {TypeName}Copier.CloneStatic(source)
    }

    private sealed class ParameterModel(
        string name,
        string fqTypeName,
        CloneKind cloneKind,
        string? elementFqTypeName = null,
        string? valueFqTypeName = null,
        string? copierFqTypeName = null)
    {
        public string Name { get; } = name;
        public string FqTypeName { get; } = fqTypeName;
        public CloneKind CloneKind { get; } = cloneKind;
        public string? ElementFqTypeName { get; } = elementFqTypeName;
        public string? ValueFqTypeName { get; } = valueFqTypeName;
        public string? CopierFqTypeName { get; } = copierFqTypeName;
    }
}
