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
    private const string IGrainWithStringKeyFqn  = "Quark.Core.Abstractions.Grains.IGrainWithStringKey";
    private const string IGrainWithGuidKeyFqn    = "Quark.Core.Abstractions.Grains.IGrainWithGuidKey";
    private const string IGrainWithIntegerKeyFqn = "Quark.Core.Abstractions.Grains.IGrainWithIntegerKey";

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
    // Parameter clone & serialize strategy
    // -----------------------------------------------------------------------

    private static ParameterModel BuildParameterModel(IParameterSymbol param)
    {
        ITypeSymbol type = param.Type;
        string name = param.Name;
        string fqTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // ---- Clone strategy ------------------------------------------------

        CloneKind cloneKind = CloneKind.None;
        string? elementFqTypeName = null;
        string? valueFqTypeName = null;
        string? copierFqTypeName = null;

        if (!type.IsValueType && type.SpecialType != SpecialType.System_String
            && !HasAttribute(type, ImmutableAttributeFqn))
        {
            // IReadOnly* interfaces — no clone needed
            bool isReadOnly = false;
            if (type is INamedTypeSymbol readOnlyNamed && readOnlyNamed.IsGenericType)
            {
                string def = readOnlyNamed.ConstructedFrom.ToDisplayString();
                if (def is "System.Collections.Generic.IReadOnlyList<T>"
                        or "System.Collections.Generic.IReadOnlyCollection<T>"
                        or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                        or "System.Collections.Generic.IReadOnlySet<T>")
                    isReadOnly = true;
            }

            if (!isReadOnly)
            {
                if (type is INamedTypeSymbol genericNamed && genericNamed.IsGenericType)
                {
                    string def = genericNamed.ConstructedFrom.ToDisplayString();
                    if (def is "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>")
                    {
                        elementFqTypeName = genericNamed.TypeArguments[0]
                            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        cloneKind = CloneKind.NewList;
                    }
                    else if (def == "System.Collections.Generic.Dictionary<TKey, TValue>")
                    {
                        elementFqTypeName = genericNamed.TypeArguments[0]
                            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        valueFqTypeName = genericNamed.TypeArguments[1]
                            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        cloneKind = CloneKind.NewDictionary;
                    }
                }
                else if (type is IArrayTypeSymbol arrayType)
                {
                    elementFqTypeName = arrayType.ElementType
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    cloneKind = CloneKind.NewArray;
                }
                else if (HasAttribute(type, GenerateSerializerFqn) && type is INamedTypeSymbol gsNamed)
                {
                    string ns2 = gsNamed.ContainingNamespace.IsGlobalNamespace
                        ? "global::"
                        : $"global::{gsNamed.ContainingNamespace.ToDisplayString()}.";
                    copierFqTypeName = $"{ns2}{gsNamed.Name}Copier";
                    cloneKind = CloneKind.GeneratedCopier;
                }
            }
        }

        // ---- Serialize strategy --------------------------------------------

        var serInfo = DetermineSerializeKind(type);

        return new ParameterModel(
            name, fqTypeName,
            cloneKind,
            elementFqTypeName: elementFqTypeName,
            valueFqTypeName: valueFqTypeName,
            copierFqTypeName: copierFqTypeName ?? serInfo.CopierFq,
            serializeKind: serInfo.Kind,
            elementSerializeKind: serInfo.ElementKind,
            valueSerializeKind: serInfo.ValueKind,
            elementCopierFqTypeName: serInfo.ElementCopierFq,
            valueCopierFqTypeName: serInfo.ValueCopierFq);
    }

    private static SerializeInfo DetermineSerializeKind(ITypeSymbol type)
    {
        // Well-known primitive types
        SerializeKind kind = type.SpecialType switch
        {
            SpecialType.System_Boolean => SerializeKind.Bool,
            SpecialType.System_Byte    => SerializeKind.UInt8,
            SpecialType.System_SByte   => SerializeKind.Int8,
            SpecialType.System_Char    => SerializeKind.UInt16,
            SpecialType.System_Int16   => SerializeKind.Int16,
            SpecialType.System_UInt16  => SerializeKind.UInt16,
            SpecialType.System_Int32   => SerializeKind.Int32,
            SpecialType.System_UInt32  => SerializeKind.UInt32,
            SpecialType.System_Int64   => SerializeKind.Int64,
            SpecialType.System_UInt64  => SerializeKind.UInt64,
            SpecialType.System_Single  => SerializeKind.Float,
            SpecialType.System_Double  => SerializeKind.Double,
            SpecialType.System_String  => SerializeKind.String,
            _                          => SerializeKind.Fallback
        };
        if (kind != SerializeKind.Fallback) return new SerializeInfo(kind);

        // Guid
        if (type.ToDisplayString() == "System.Guid")
            return new SerializeInfo(SerializeKind.Guid);

        // byte[] — array of System.Byte
        if (type is IArrayTypeSymbol byteArr && byteArr.ElementType.SpecialType == SpecialType.System_Byte)
            return new SerializeInfo(SerializeKind.Bytes);

        // Generic collections — List<T> / IList<T>
        if (type is INamedTypeSymbol namedList && namedList.IsGenericType)
        {
            string def = namedList.ConstructedFrom.ToDisplayString();
            if (def is "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>")
            {
                var elemInfo = DetermineSerializeKind(namedList.TypeArguments[0]);
                if (elemInfo.Kind != SerializeKind.Fallback)
                    return new SerializeInfo(SerializeKind.List,
                        elementKind: elemInfo.Kind,
                        elementCopierFq: elemInfo.CopierFq);
            }

            if (def == "System.Collections.Generic.Dictionary<TKey, TValue>")
            {
                var keyInfo = DetermineSerializeKind(namedList.TypeArguments[0]);
                var valInfo = DetermineSerializeKind(namedList.TypeArguments[1]);
                if (keyInfo.Kind != SerializeKind.Fallback && valInfo.Kind != SerializeKind.Fallback)
                    return new SerializeInfo(SerializeKind.Dictionary,
                        elementKind: keyInfo.Kind,
                        valueKind: valInfo.Kind,
                        elementCopierFq: keyInfo.CopierFq,
                        valueCopierFq: valInfo.CopierFq);
            }
        }

        // T[] (non-byte element)
        if (type is IArrayTypeSymbol arr)
        {
            var elemInfo = DetermineSerializeKind(arr.ElementType);
            if (elemInfo.Kind != SerializeKind.Fallback)
                return new SerializeInfo(SerializeKind.Array,
                    elementKind: elemInfo.Kind,
                    elementCopierFq: elemInfo.CopierFq);
        }

        // [GenerateSerializer] types — use generated {TypeName}Copier.WriteStatic / ReadStatic
        if (HasAttribute(type, GenerateSerializerFqn) && type is INamedTypeSymbol gsNamed)
        {
            string ns = gsNamed.ContainingNamespace.IsGlobalNamespace
                ? "global::"
                : $"global::{gsNamed.ContainingNamespace.ToDisplayString()}.";
            string copierFq = $"{ns}{gsNamed.Name}Copier";
            return new SerializeInfo(SerializeKind.GeneratedCodec, copierFq: copierFq);
        }

        // Grain-ref parameters — type implements IGrain
        if (type is INamedTypeSymbol grainNamed)
        {
            bool isGrain = false;
            SerializeKind grainRefKind = SerializeKind.GrainRefString; // default
            foreach (INamedTypeSymbol iface in grainNamed.AllInterfaces)
            {
                string ifaceFqn = iface.ToDisplayString();
                if (ifaceFqn == IGrainFqn)               isGrain = true;
                if (ifaceFqn == IGrainWithGuidKeyFqn)    grainRefKind = SerializeKind.GrainRefGuid;
                if (ifaceFqn == IGrainWithIntegerKeyFqn) grainRefKind = SerializeKind.GrainRefInteger;
            }
            if (isGrain) return new SerializeInfo(grainRefKind);
        }

        return new SerializeInfo(SerializeKind.Fallback);
    }

    // Returns the single-line write expression for primitive/string/Guid/Bytes/GeneratedCodec.
    // Not used for collection types (those are emitted inline with loops).
    private static string GetWriteExpr(SerializeKind kind, string valueExpr, string? copierFq, string fqTypeName)
        => kind switch
        {
            SerializeKind.Bool    => $"writer.WriteByte({valueExpr} ? (byte)1 : (byte)0);",
            SerializeKind.UInt8   => $"writer.WriteByte((byte){valueExpr});",
            SerializeKind.Int8    => $"writer.WriteInt32((int){valueExpr});",
            SerializeKind.Int16   => $"writer.WriteInt32((int){valueExpr});",
            SerializeKind.UInt16  => $"writer.WriteVarUInt32((uint){valueExpr});",
            SerializeKind.Int32   => $"writer.WriteInt32({valueExpr});",
            SerializeKind.UInt32  => $"writer.WriteVarUInt32({valueExpr});",
            SerializeKind.Int64   => $"writer.WriteInt64({valueExpr});",
            SerializeKind.UInt64  => $"writer.WriteVarUInt64({valueExpr});",
            SerializeKind.Float   => $"writer.WriteFixed32(unchecked((uint)global::System.BitConverter.SingleToInt32Bits({valueExpr})));",
            SerializeKind.Double  => $"writer.WriteFixed64(unchecked((ulong)global::System.BitConverter.DoubleToInt64Bits({valueExpr})));",
            SerializeKind.String  => $"writer.WriteString({valueExpr});",
            SerializeKind.Bytes   => $"writer.WriteBytes({valueExpr} ?? global::System.Array.Empty<byte>());",
            SerializeKind.Guid    => $"writer.WriteRaw({valueExpr}.ToByteArray());",
            SerializeKind.GeneratedCodec => $"{copierFq}.WriteStatic(ref writer, {valueExpr});",
            SerializeKind.GrainRefString
                or SerializeKind.GrainRefGuid
                or SerializeKind.GrainRefInteger =>
                $"writer.WriteString(((global::Quark.Core.Abstractions.Hosting.IGrainProxy){valueExpr}).GrainId.Key);",
            _ => $"global::Quark.Runtime.GrainMessageSerializer.WriteValue(writer, {valueExpr});"
        };

    // Returns the single-line read expression (the value, not assignment) for
    // primitive/string/Guid/Bytes/GeneratedCodec. Cast uses fqTypeName for narrow int types.
    private static string GetReadExpr(SerializeKind kind, string? copierFq, string fqTypeName)
        => kind switch
        {
            SerializeKind.Bool    => "reader.ReadByte() != 0",
            SerializeKind.UInt8   => "reader.ReadByte()",
            SerializeKind.Int8
                or SerializeKind.Int16  => $"({fqTypeName})reader.ReadInt32()",
            SerializeKind.UInt16  => $"({fqTypeName})reader.ReadVarUInt32()",
            SerializeKind.Int32   => "reader.ReadInt32()",
            SerializeKind.UInt32  => "reader.ReadVarUInt32()",
            SerializeKind.Int64   => "reader.ReadInt64()",
            SerializeKind.UInt64  => "reader.ReadVarUInt64()",
            SerializeKind.Float   => "global::System.BitConverter.Int32BitsToSingle(unchecked((int)reader.ReadFixed32()))",
            SerializeKind.Double  => "global::System.BitConverter.Int64BitsToDouble(unchecked((long)reader.ReadFixed64()))",
            SerializeKind.String  => "reader.ReadString()",
            SerializeKind.Bytes   => "reader.ReadBytes()",
            SerializeKind.Guid    => "new global::System.Guid(reader.ReadRaw(16))",
            SerializeKind.GeneratedCodec => $"{copierFq}.ReadStatic(ref reader)!",
            SerializeKind.GrainRefString   => $"factory!.GetGrain<{fqTypeName}>(reader.ReadString())",
            SerializeKind.GrainRefGuid     => $"factory!.GetGrain<{fqTypeName}>(global::System.Guid.ParseExact(reader.ReadString(), \"N\"))",
            SerializeKind.GrainRefInteger  => $"factory!.GetGrain<{fqTypeName}>(long.Parse(reader.ReadString()))",
            _ => $"({fqTypeName})global::Quark.Runtime.GrainMessageSerializer.ReadArg(ref reader)!"
        };

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
        if (!m.IsObserver)
        {
            sb.AppendLine("    , global::Quark.Core.Abstractions.Hosting.IGrainProxy");
        }
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::Quark.Core.Abstractions.Identity.GrainId _grainId;");
        sb.AppendLine("    private readonly global::Quark.Core.Abstractions.Hosting.IGrainCallInvoker _invoker;");
        if (!m.IsObserver)
        {
            sb.AppendLine();
            sb.AppendLine("    public global::Quark.Core.Abstractions.Identity.GrainId GrainId => _grainId;");
        }
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

        // Serialize(ref CodecWriter) / static Deserialize(ref CodecReader) —
        // type-specific wire encoding for the transport path (no boxing).
        EmitSerializeMethods(sb, structName, method);

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

    private static void EmitSerializeMethods(StringBuilder sb, string structName, MethodModel method)
    {
        sb.AppendLine();

        // --- Serialize(ref CodecWriter writer) ---
        sb.AppendLine("    public void Serialize(ref global::Quark.Serialization.Abstractions.Buffers.CodecWriter writer)");
        if (method.Parameters.Count == 0)
        {
            sb.AppendLine("    { }");
        }
        else
        {
            sb.AppendLine("    {");
            foreach (ParameterModel p in method.Parameters)
            {
                EmitParameterWrite(sb, p, "        ");
            }
            sb.AppendLine("    }");
        }

        sb.AppendLine();

        // --- static Deserialize(ref CodecReader reader, IGrainFactory? factory = null) ---
        sb.AppendLine($"    public static {structName} Deserialize(");
        sb.AppendLine("        ref global::Quark.Serialization.Abstractions.Buffers.CodecReader reader,");
        sb.AppendLine("        global::Quark.Core.Abstractions.Hosting.IGrainFactory? factory = null)");
        if (method.Parameters.Count == 0)
        {
            sb.AppendLine("        => new();");
        }
        else
        {
            sb.AppendLine("    {");
            foreach (ParameterModel p in method.Parameters)
            {
                EmitParameterRead(sb, p, "        ");
            }
            string ctorArgs = string.Join(", ", method.Parameters.Select(p => $"_{p.Name}"));
            sb.AppendLine($"        return new {structName}({ctorArgs});");
            sb.AppendLine("    }");
        }
    }

    // Emits the write statement(s) for a single parameter inside Serialize().
    private static void EmitParameterWrite(StringBuilder sb, ParameterModel p, string indent)
    {
        string fieldExpr = $"_{p.Name}";

        switch (p.SerializeKind)
        {
            case SerializeKind.List:
            {
                string elemWriteExpr = GetWriteExpr(p.ElementSerializeKind, "__e", p.ElementCopierFqTypeName, p.ElementFqTypeName ?? "");
                sb.AppendLine($"{indent}if ({fieldExpr} is null) {{ writer.WriteByte(0); }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    writer.WriteByte(1);");
                sb.AppendLine($"{indent}    writer.WriteVarUInt32((uint){fieldExpr}.Count);");
                sb.AppendLine($"{indent}    foreach (var __e in {fieldExpr})");
                sb.AppendLine($"{indent}        {elemWriteExpr}");
                sb.AppendLine($"{indent}}}");
                break;
            }
            case SerializeKind.Array:
            {
                string elemWriteExpr = GetWriteExpr(p.ElementSerializeKind, "__e", p.ElementCopierFqTypeName, p.ElementFqTypeName ?? "");
                sb.AppendLine($"{indent}if ({fieldExpr} is null) {{ writer.WriteByte(0); }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    writer.WriteByte(1);");
                sb.AppendLine($"{indent}    writer.WriteVarUInt32((uint){fieldExpr}.Length);");
                sb.AppendLine($"{indent}    foreach (var __e in {fieldExpr})");
                sb.AppendLine($"{indent}        {elemWriteExpr}");
                sb.AppendLine($"{indent}}}");
                break;
            }
            case SerializeKind.Dictionary:
            {
                string keyWriteExpr = GetWriteExpr(p.ElementSerializeKind, "__kv.Key", p.ElementCopierFqTypeName, p.ElementFqTypeName ?? "");
                string valWriteExpr = GetWriteExpr(p.ValueSerializeKind, "__kv.Value", p.ValueCopierFqTypeName, p.ValueFqTypeName ?? "");
                sb.AppendLine($"{indent}if ({fieldExpr} is null) {{ writer.WriteByte(0); }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    writer.WriteByte(1);");
                sb.AppendLine($"{indent}    writer.WriteVarUInt32((uint){fieldExpr}.Count);");
                sb.AppendLine($"{indent}    foreach (var __kv in {fieldExpr})");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        {keyWriteExpr}");
                sb.AppendLine($"{indent}        {valWriteExpr}");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine($"{indent}}}");
                break;
            }
            default:
            {
                string writeExpr = GetWriteExpr(p.SerializeKind, fieldExpr, p.CopierFqTypeName, p.FqTypeName);
                sb.AppendLine($"{indent}{writeExpr}");
                break;
            }
        }
    }

    // Emits the read local variable declaration(s) for a single parameter inside Deserialize().
    private static void EmitParameterRead(StringBuilder sb, ParameterModel p, string indent)
    {
        string fqType = p.FqTypeName;

        switch (p.SerializeKind)
        {
            case SerializeKind.List:
            {
                string elemFq = p.ElementFqTypeName ?? "object";
                string elemReadExpr = GetReadExpr(p.ElementSerializeKind, p.ElementCopierFqTypeName, elemFq);
                sb.AppendLine($"{indent}global::System.Collections.Generic.List<{elemFq}>? _{p.Name};");
                sb.AppendLine($"{indent}if (reader.ReadByte() == 0) {{ _{p.Name} = null; }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    uint __n_{p.Name} = reader.ReadVarUInt32();");
                sb.AppendLine($"{indent}    _{p.Name} = new((int)__n_{p.Name});");
                sb.AppendLine($"{indent}    for (uint __i = 0; __i < __n_{p.Name}; __i++)");
                sb.AppendLine($"{indent}        _{p.Name}.Add({elemReadExpr});");
                sb.AppendLine($"{indent}}}");
                break;
            }
            case SerializeKind.Array:
            {
                string elemFq = p.ElementFqTypeName ?? "object";
                string elemReadExpr = GetReadExpr(p.ElementSerializeKind, p.ElementCopierFqTypeName, elemFq);
                sb.AppendLine($"{indent}{elemFq}[]? _{p.Name};");
                sb.AppendLine($"{indent}if (reader.ReadByte() == 0) {{ _{p.Name} = null; }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    uint __n_{p.Name} = reader.ReadVarUInt32();");
                sb.AppendLine($"{indent}    _{p.Name} = new {elemFq}[__n_{p.Name}];");
                sb.AppendLine($"{indent}    for (uint __i = 0; __i < __n_{p.Name}; __i++)");
                sb.AppendLine($"{indent}        _{p.Name}[__i] = {elemReadExpr};");
                sb.AppendLine($"{indent}}}");
                break;
            }
            case SerializeKind.Dictionary:
            {
                string keyFq = p.ElementFqTypeName ?? "object";
                string valFq = p.ValueFqTypeName ?? "object";
                string keyReadExpr = GetReadExpr(p.ElementSerializeKind, p.ElementCopierFqTypeName, keyFq);
                string valReadExpr = GetReadExpr(p.ValueSerializeKind, p.ValueCopierFqTypeName, valFq);
                sb.AppendLine($"{indent}global::System.Collections.Generic.Dictionary<{keyFq}, {valFq}>? _{p.Name};");
                sb.AppendLine($"{indent}if (reader.ReadByte() == 0) {{ _{p.Name} = null; }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    uint __n_{p.Name} = reader.ReadVarUInt32();");
                sb.AppendLine($"{indent}    _{p.Name} = new((int)__n_{p.Name});");
                sb.AppendLine($"{indent}    for (uint __i = 0; __i < __n_{p.Name}; __i++)");
                sb.AppendLine($"{indent}        _{p.Name}[{keyReadExpr}] = {valReadExpr};");
                sb.AppendLine($"{indent}}}");
                break;
            }
            default:
            {
                string readExpr = GetReadExpr(p.SerializeKind, p.CopierFqTypeName, fqType);
                sb.AppendLine($"{indent}{fqType} _{p.Name} = {readExpr};");
                break;
            }
        }
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
        sb.AppendLine("        global::Quark.Core.Abstractions.Hosting.IGrainFactory? factory,");
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
                sb.AppendLine($"                var invokable = {structName}.Deserialize(ref _reader, factory);");
            }
            else
            {
                sb.AppendLine($"                var invokable = new {structName}();");
            }

            if (m.IsObserver)
            {
                sb.AppendLine($"                await invoker.InvokeObserverAsync<{structName}>(grainId, invokable, ct).ConfigureAwait(false);");
                sb.AppendLine("                return null;");
            }
            else if (method.IsTask || method.IsValueTask)
            {
                sb.AppendLine($"                await invoker.InvokeVoidAsync<{structName}>(grainId, invokable, ct).ConfigureAwait(false);");
                sb.AppendLine("                return null;");
            }
            else
            {
                sb.AppendLine($"                return await invoker.InvokeAsync<{structName}, {method.TaskResultType}>(grainId, invokable, ct).ConfigureAwait(false);");
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

    private enum SerializeKind
    {
        Bool,
        Int8, Int16, Int32, Int64,
        UInt8, UInt16, UInt32, UInt64,
        Float, Double,
        String, Bytes, Guid,
        List, Array, Dictionary,
        GeneratedCodec,
        GrainRefString,
        GrainRefGuid,
        GrainRefInteger,
        Fallback
    }

    private struct SerializeInfo
    {
        public SerializeKind Kind;
        public SerializeKind ElementKind;
        public SerializeKind ValueKind;
        public string? CopierFq;
        public string? ElementCopierFq;
        public string? ValueCopierFq;

        public SerializeInfo(
            SerializeKind kind,
            SerializeKind elementKind = SerializeKind.Fallback,
            SerializeKind valueKind = SerializeKind.Fallback,
            string? copierFq = null,
            string? elementCopierFq = null,
            string? valueCopierFq = null)
        {
            Kind = kind;
            ElementKind = elementKind;
            ValueKind = valueKind;
            CopierFq = copierFq;
            ElementCopierFq = elementCopierFq;
            ValueCopierFq = valueCopierFq;
        }
    }

    private sealed class ParameterModel(
        string name,
        string fqTypeName,
        CloneKind cloneKind,
        string? elementFqTypeName = null,
        string? valueFqTypeName = null,
        string? copierFqTypeName = null,
        SerializeKind serializeKind = SerializeKind.Fallback,
        SerializeKind elementSerializeKind = SerializeKind.Fallback,
        SerializeKind valueSerializeKind = SerializeKind.Fallback,
        string? elementCopierFqTypeName = null,
        string? valueCopierFqTypeName = null)
    {
        public string Name { get; } = name;
        public string FqTypeName { get; } = fqTypeName;
        public CloneKind CloneKind { get; } = cloneKind;
        public string? ElementFqTypeName { get; } = elementFqTypeName;
        public string? ValueFqTypeName { get; } = valueFqTypeName;
        public string? CopierFqTypeName { get; } = copierFqTypeName;
        public SerializeKind SerializeKind { get; } = serializeKind;
        public SerializeKind ElementSerializeKind { get; } = elementSerializeKind;
        public SerializeKind ValueSerializeKind { get; } = valueSerializeKind;
        public string? ElementCopierFqTypeName { get; } = elementCopierFqTypeName;
        public string? ValueCopierFqTypeName { get; } = valueCopierFqTypeName;
    }
}
