using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Quark.CodeGenerator;

/// <summary>
///     Source generator that emits <c>IFieldCodec&lt;T&gt;</c> and <c>IDeepCopier&lt;T&gt;</c>
///     implementations for every type annotated with <c>[GenerateSerializer]</c>.
///     For each serializable member (property/field tagged <c>[Id(N)]</c>) the generator:
///     <list type="bullet">
///         <item>Writes/reads via the codec resolved from <c>ICodecProvider</c>.</item>
///         <item>Uses field-id switch dispatch on deserialisation; skips unknown fields.</item>
///     </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SerializerGenerator : IIncrementalGenerator
{
    private const string GenerateSerializerFqn =
        "Quark.Serialization.Abstractions.Attributes.GenerateSerializerAttribute";

    private const string IdAttributeFqn =
        "Quark.Serialization.Abstractions.Attributes.IdAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<TypeModel?> models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GenerateSerializerFqn,
                static (node, _) => node is TypeDeclarationSyntax,
                ExtractModel);

        context.RegisterSourceOutput(
            models.Where(static m => m is not null).Collect(),
            static (ctx, items) => Emit(ctx, items!));
    }

    // -----------------------------------------------------------------------
    // Model extraction (runs in incremental pipeline — no Roslyn types stored)
    // -----------------------------------------------------------------------

    private static TypeModel? ExtractModel(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        // Skip generic types for now — future M2 work.
        if (typeSymbol.IsGenericType)
        {
            return null;
        }

        string ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var members = new List<MemberModel>();
        foreach (ISymbol? member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            foreach (AttributeData? attr in member.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != IdAttributeFqn)
                {
                    continue;
                }

                if (attr.ConstructorArguments.Length == 0)
                {
                    continue;
                }

                object? idValue = attr.ConstructorArguments[0].Value;
                uint id = idValue is uint u ? u : (uint)(int)idValue!;

                ITypeSymbol memberType;
                bool isProperty;
                if (member is IPropertySymbol prop && !prop.IsStatic && prop.SetMethod is not null)
                {
                    memberType = prop.Type;
                    isProperty = true;
                }
                else if (member is IFieldSymbol field && !field.IsStatic && !field.IsReadOnly)
                {
                    memberType = field.Type;
                    isProperty = false;
                }
                else
                {
                    continue;
                }

                var (serKind, copierFq) = GetMemberSerializeInfo(memberType);

                members.Add(new MemberModel(
                    id,
                    member.Name,
                    memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    isProperty,
                    serKind,
                    copierFq));
            }
        }

        if (members.Count == 0)
        {
            return null;
        }

        return new TypeModel(
            ns,
            typeSymbol.Name,
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            members.OrderBy(m => m.Id).ToList(),
            typeSymbol.IsValueType);
    }

    private static (MemberSerializeKind kind, string? copierFq) GetMemberSerializeInfo(ITypeSymbol type)
    {
        MemberSerializeKind kind = type.SpecialType switch
        {
            SpecialType.System_Boolean => MemberSerializeKind.Bool,
            SpecialType.System_Byte    => MemberSerializeKind.UInt8,
            SpecialType.System_SByte   => MemberSerializeKind.Int8,
            SpecialType.System_Char    => MemberSerializeKind.UInt16,
            SpecialType.System_Int16   => MemberSerializeKind.Int16,
            SpecialType.System_UInt16  => MemberSerializeKind.UInt16,
            SpecialType.System_Int32   => MemberSerializeKind.Int32,
            SpecialType.System_UInt32  => MemberSerializeKind.UInt32,
            SpecialType.System_Int64   => MemberSerializeKind.Int64,
            SpecialType.System_UInt64  => MemberSerializeKind.UInt64,
            SpecialType.System_Single  => MemberSerializeKind.Float,
            SpecialType.System_Double  => MemberSerializeKind.Double,
            SpecialType.System_String  => MemberSerializeKind.String,
            _                          => MemberSerializeKind.Fallback
        };

        if (kind != MemberSerializeKind.Fallback) return (kind, null);

        // Guid
        if (type.ToDisplayString() == "System.Guid")
            return (MemberSerializeKind.Guid, null);

        // [GenerateSerializer] — emit {TypeName}Copier.WriteStatic / ReadStatic
        if (type is INamedTypeSymbol named)
        {
            foreach (AttributeData attr in type.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == GenerateSerializerFqn)
                {
                    string nsPrefix = named.ContainingNamespace.IsGlobalNamespace
                        ? "global::"
                        : $"global::{named.ContainingNamespace.ToDisplayString()}.";
                    return (MemberSerializeKind.GeneratedCodec, $"{nsPrefix}{named.Name}Copier");
                }
            }
        }

        return (MemberSerializeKind.Fallback, null);
    }

    // -----------------------------------------------------------------------
    // Code emission
    // -----------------------------------------------------------------------

    private static void Emit(
        SourceProductionContext ctx,
        ImmutableArray<TypeModel> models)
    {
        foreach (TypeModel model in models)
        {
            string source = BuildCodecSource(model);
            ctx.AddSource(
                $"{model.TypeName}.QuarkSerializer.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string BuildCodecSource(TypeModel m)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by Quark.CodeGenerator — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine();

        bool hasNs = !string.IsNullOrEmpty(m.Namespace);
        if (hasNs)
        {
            sb.AppendLine($"namespace {m.Namespace};");
            sb.AppendLine();
        }

        // ---- IFieldCodec<T> ------------------------------------------------

        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Quark.CodeGenerator\", \"0.1.0\")]");
        sb.AppendLine($"internal sealed class {m.TypeName}Codec");
        sb.AppendLine($"    : global::Quark.Serialization.Abstractions.Abstractions.IFieldCodec<{m.FqTypeName}>");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::Quark.Serialization.Abstractions.Abstractions.ICodecProvider _codecs;");
        sb.AppendLine();
        sb.AppendLine($"    public {m.TypeName}Codec(global::Quark.Serialization.Abstractions.Abstractions.ICodecProvider codecs)");
        sb.AppendLine("        => _codecs = codecs;");
        sb.AppendLine();

        // WriteField
        sb.AppendLine("    public void WriteField(");
        sb.AppendLine("        global::Quark.Serialization.Abstractions.Buffers.CodecWriter writer,");
        sb.AppendLine("        uint fieldId,");
        sb.AppendLine("        global::System.Type expectedType,");
        sb.AppendLine($"        {m.FqTypeName} value)");
        sb.AppendLine("    {");
        if (!m.IsValueType)
        {
            sb.AppendLine("        if (value is null)");
            sb.AppendLine("        {");
            sb.AppendLine(
                "            writer.WriteFieldHeader(fieldId, global::Quark.Serialization.Abstractions.Buffers.WireType.Extended);");
            sb.AppendLine(
                "            writer.WriteByte((byte)global::Quark.Serialization.Abstractions.Buffers.ExtendedWireType.Null);");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
        }

        sb.AppendLine(
            "        writer.WriteFieldHeader(fieldId, global::Quark.Serialization.Abstractions.Buffers.WireType.TagDelimited);");
        foreach (MemberModel member in m.Members)
        {
            sb.AppendLine(
                $"        _codecs.GetRequiredCodec<{member.FqTypeName}>().WriteField(writer, {member.Id}u, typeof({member.FqTypeName}), value.{member.Name});");
        }

        sb.AppendLine(
            "        writer.WriteFieldHeader(0u, global::Quark.Serialization.Abstractions.Buffers.WireType.EndTagDelimited);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ReadValue
        sb.AppendLine($"    public {m.FqTypeName} ReadValue(");
        sb.AppendLine("        global::Quark.Serialization.Abstractions.Buffers.CodecReader reader,");
        sb.AppendLine("        global::Quark.Serialization.Abstractions.Buffers.Field field)");
        sb.AppendLine("    {");
        if (!m.IsValueType)
        {
            sb.AppendLine("        if (field.WireType == global::Quark.Serialization.Abstractions.Buffers.WireType.Extended)");
            sb.AppendLine("        {");
            sb.AppendLine("            return default!;");
            sb.AppendLine("        }");
        }

        sb.AppendLine($"        var result = new {m.FqTypeName}();");
        sb.AppendLine("        global::Quark.Serialization.Abstractions.Buffers.Field f;");
        sb.AppendLine("        while (!(f = reader.ReadFieldHeader()).IsEndObject)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch ((int)f.FieldId)");
        sb.AppendLine("            {");
        foreach (MemberModel member in m.Members)
        {
            sb.AppendLine(
                $"                case {member.Id}: result.{member.Name} = _codecs.GetRequiredCodec<{member.FqTypeName}>().ReadValue(reader, f); break;");
        }

        sb.AppendLine("                default: SkipField(reader, f); break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // SkipField helper
        sb.AppendLine("    private static void SkipField(");
        sb.AppendLine("        global::Quark.Serialization.Abstractions.Buffers.CodecReader reader,");
        sb.AppendLine("        global::Quark.Serialization.Abstractions.Buffers.Field field)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (field.WireType)");
        sb.AppendLine("        {");
        sb.AppendLine("            case global::Quark.Serialization.Abstractions.Buffers.WireType.VarInt:");
        sb.AppendLine("                reader.ReadVarUInt64(); break;");
        sb.AppendLine("            case global::Quark.Serialization.Abstractions.Buffers.WireType.Fixed32:");
        sb.AppendLine("                reader.ReadFixed32(); break;");
        sb.AppendLine("            case global::Quark.Serialization.Abstractions.Buffers.WireType.Fixed64:");
        sb.AppendLine("                reader.ReadFixed64(); break;");
        sb.AppendLine("            case global::Quark.Serialization.Abstractions.Buffers.WireType.LengthPrefixed:");
        sb.AppendLine("                reader.ReadBytes(); break;");
        sb.AppendLine("            case global::Quark.Serialization.Abstractions.Buffers.WireType.TagDelimited:");
        sb.AppendLine("                global::Quark.Serialization.Abstractions.Buffers.Field nested;");
        sb.AppendLine("                while (!(nested = reader.ReadFieldHeader()).IsEndObject)");
        sb.AppendLine("                {");
        sb.AppendLine("                    SkipField(reader, nested);");
        sb.AppendLine("                }");
        sb.AppendLine("                break;");
        sb.AppendLine("            case global::Quark.Serialization.Abstractions.Buffers.WireType.Extended:");
        sb.AppendLine("            case global::Quark.Serialization.Abstractions.Buffers.WireType.EndTagDelimited:");
        sb.AppendLine("                break;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // ---- IDeepCopier<T> ------------------------------------------------
        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Quark.CodeGenerator\", \"0.1.0\")]");
        sb.AppendLine($"internal sealed class {m.TypeName}Copier");
        sb.AppendLine($"    : global::Quark.Serialization.Abstractions.Abstractions.IDeepCopier<{m.FqTypeName}>");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::Quark.Serialization.Abstractions.Abstractions.ICopierProvider _copiers;");
        sb.AppendLine();
        sb.AppendLine(
            $"    public {m.TypeName}Copier(global::Quark.Serialization.Abstractions.Abstractions.ICopierProvider copiers)");
        sb.AppendLine("        => _copiers = copiers;");
        sb.AppendLine();
        sb.AppendLine($"    public {m.FqTypeName} DeepCopy({m.FqTypeName} input,");
        sb.AppendLine("        global::Quark.Serialization.Abstractions.Abstractions.CopyContext context)");
        sb.AppendLine("    {");
        if (!m.IsValueType)
        {
            sb.AppendLine("        if (input is null) return default!;");
            sb.AppendLine($"        var existing = context.TryGetCopy<{m.FqTypeName}>(input);");
            sb.AppendLine("        if (existing is not null) return existing;");
        }

        sb.AppendLine($"        var copy = new {m.FqTypeName}();");
        if (!m.IsValueType)
        {
            sb.AppendLine("        context.RecordCopy(input, copy);");
        }

        foreach (MemberModel member in m.Members)
        {
            sb.AppendLine(
                $"        copy.{member.Name} = _copiers.GetRequiredCopier<{member.FqTypeName}>().DeepCopy(input.{member.Name}, context);");
        }

        sb.AppendLine("        return copy;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CloneStatic — DI-free shallow clone used by generated invokable Clone() methods.
        // Copies value-type and string members directly; reference-type members get a direct
        // reference copy (shallow). Generated invokable Clone() calls this for [GenerateSerializer]
        // parameter types so grain args are isolated without requiring DI access in a struct.
        sb.AppendLine($"    public static {m.FqTypeName} CloneStatic({m.FqTypeName} input)");
        sb.AppendLine("    {");
        if (!m.IsValueType)
        {
            sb.AppendLine("        if (input is null) return default!;");
        }

        sb.AppendLine($"        return new {m.FqTypeName}");
        sb.AppendLine("        {");
        foreach (MemberModel member in m.Members)
        {
            sb.AppendLine($"            {member.Name} = input.{member.Name},");
        }
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // WriteStatic / ReadStatic — DI-free positional binary encoding used by generated
        // invokable Serialize() / Deserialize() methods on the transport path.
        // Members are written/read in [Id]-ascending order using direct CodecWriter/CodecReader calls.
        string nullableSuffix = m.IsValueType ? "" : "?";
        sb.AppendLine($"    public static void WriteStatic(");
        sb.AppendLine("        ref global::Quark.Serialization.Abstractions.Buffers.CodecWriter writer,");
        sb.AppendLine($"        {m.FqTypeName}{nullableSuffix} value)");
        sb.AppendLine("    {");
        if (!m.IsValueType)
        {
            sb.AppendLine("        if (value is null) { writer.WriteByte(0); return; }");
            sb.AppendLine("        writer.WriteByte(1);");
        }
        foreach (MemberModel member in m.Members)
        {
            EmitMemberWrite(sb, member);
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine($"    public static {m.FqTypeName}{nullableSuffix} ReadStatic(");
        sb.AppendLine("        ref global::Quark.Serialization.Abstractions.Buffers.CodecReader reader)");
        sb.AppendLine("    {");
        if (!m.IsValueType)
        {
            sb.AppendLine("        if (reader.ReadByte() == 0) return null;");
        }
        sb.AppendLine($"        return new {m.FqTypeName}");
        sb.AppendLine("        {");
        foreach (MemberModel member in m.Members)
        {
            EmitMemberRead(sb, member);
        }
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitMemberWrite(StringBuilder sb, MemberModel member)
    {
        string val = $"value.{member.Name}";
        string expr = member.SerializeKind switch
        {
            MemberSerializeKind.Bool    => $"writer.WriteByte({val} ? (byte)1 : (byte)0);",
            MemberSerializeKind.UInt8   => $"writer.WriteByte((byte){val});",
            MemberSerializeKind.Int8    => $"writer.WriteInt32((int){val});",
            MemberSerializeKind.Int16   => $"writer.WriteInt32((int){val});",
            MemberSerializeKind.UInt16  => $"writer.WriteVarUInt32((uint){val});",
            MemberSerializeKind.Int32   => $"writer.WriteInt32({val});",
            MemberSerializeKind.UInt32  => $"writer.WriteVarUInt32({val});",
            MemberSerializeKind.Int64   => $"writer.WriteInt64({val});",
            MemberSerializeKind.UInt64  => $"writer.WriteVarUInt64({val});",
            MemberSerializeKind.Float   => $"writer.WriteFixed32(unchecked((uint)global::System.BitConverter.SingleToInt32Bits({val})));",
            MemberSerializeKind.Double  => $"writer.WriteFixed64(unchecked((ulong)global::System.BitConverter.DoubleToInt64Bits({val})));",
            MemberSerializeKind.String  => $"writer.WriteString({val});",
            MemberSerializeKind.Guid    => $"writer.WriteRaw({val}.ToByteArray());",
            MemberSerializeKind.GeneratedCodec => $"{member.CopierFqTypeName}.WriteStatic(ref writer, {val});",
            _ => $"global::Quark.Runtime.GrainMessageSerializer.WriteValue(writer, {val});"
        };
        sb.AppendLine($"        {expr}");
    }

    private static void EmitMemberRead(StringBuilder sb, MemberModel member)
    {
        string readExpr = member.SerializeKind switch
        {
            MemberSerializeKind.Bool    => "reader.ReadByte() != 0",
            MemberSerializeKind.UInt8   => "reader.ReadByte()",
            MemberSerializeKind.Int8
                or MemberSerializeKind.Int16  => $"({member.FqTypeName})reader.ReadInt32()",
            MemberSerializeKind.UInt16  => $"({member.FqTypeName})reader.ReadVarUInt32()",
            MemberSerializeKind.Int32   => "reader.ReadInt32()",
            MemberSerializeKind.UInt32  => "reader.ReadVarUInt32()",
            MemberSerializeKind.Int64   => "reader.ReadInt64()",
            MemberSerializeKind.UInt64  => "reader.ReadVarUInt64()",
            MemberSerializeKind.Float   => "global::System.BitConverter.Int32BitsToSingle(unchecked((int)reader.ReadFixed32()))",
            MemberSerializeKind.Double  => "global::System.BitConverter.Int64BitsToDouble(unchecked((long)reader.ReadFixed64()))",
            MemberSerializeKind.String  => "reader.ReadString()",
            MemberSerializeKind.Guid    => "new global::System.Guid(reader.ReadRaw(16))",
            MemberSerializeKind.GeneratedCodec => $"{member.CopierFqTypeName}.ReadStatic(ref reader)!",
            _ => $"({member.FqTypeName})global::Quark.Runtime.GrainMessageSerializer.ReadArg(ref reader)!"
        };
        sb.AppendLine($"            {member.Name} = {readExpr},");
    }

    // -----------------------------------------------------------------------
    // Data models (no Roslyn references — safe to cache across incremental steps)
    // -----------------------------------------------------------------------

    private sealed class TypeModel(
        string @namespace,
        string typeName,
        string fqTypeName,
        IReadOnlyList<MemberModel> members,
        bool isValueType)
    {
        public string Namespace { get; } = @namespace;
        public string TypeName { get; } = typeName;
        public string FqTypeName { get; } = fqTypeName;
        public IReadOnlyList<MemberModel> Members { get; } = members;
        public bool IsValueType { get; } = isValueType;
    }

    private enum MemberSerializeKind
    {
        Bool,
        Int8, Int16, Int32, Int64,
        UInt8, UInt16, UInt32, UInt64,
        Float, Double,
        String, Guid,
        GeneratedCodec,
        Fallback
    }

    private sealed class MemberModel(
        uint id,
        string name,
        string fqTypeName,
        bool isProperty,
        MemberSerializeKind serializeKind = MemberSerializeKind.Fallback,
        string? copierFqTypeName = null)
    {
        public uint Id { get; } = id;
        public string Name { get; } = name;
        public string FqTypeName { get; } = fqTypeName;
        public bool IsProperty { get; } = isProperty;
        public MemberSerializeKind SerializeKind { get; } = serializeKind;
        public string? CopierFqTypeName { get; } = copierFqTypeName;
    }
}
