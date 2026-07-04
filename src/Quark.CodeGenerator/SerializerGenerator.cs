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

    internal static readonly DiagnosticDescriptor UnsupportedCollectionElementType = new(
        id: "QRK0054",
        title: "Unsupported element type in collection member",
        messageFormat: "Member '{0}' has a collection element/key/value type '{1}' that is not supported for serialization",
        category: "Quark.CodeGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<TypeModel?> models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GenerateSerializerFqn,
                Predicate,
                ExtractModel);

        context.RegisterSourceOutput(
            models.Where(static m => m is not null).Collect(),
            static (ctx, items) => Emit(ctx, items!));
    }

    private static bool Predicate(SyntaxNode node, CancellationToken _)
    {
        return node is TypeDeclarationSyntax;
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
        var diagnostics = new List<Diagnostic>();

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
                if (member is IPropertySymbol { IsStatic: false, SetMethod: not null } prop)
                {
                    memberType = prop.Type;
                    isProperty = true;
                }
                else if (member is IFieldSymbol { IsStatic: false, IsReadOnly: false } field)
                {
                    memberType = field.Type;
                    isProperty = false;
                }
                else
                {
                    continue;
                }

                Location memberLoc = member.Locations.FirstOrDefault() ?? Location.None;
                SerializeInfo info = ResolveSerializeInfo(memberType, member.Name, memberLoc, diagnostics);

                if (info.Kind == MemberSerializeKind.Invalid)
                {
                    continue;
                }

                members.Add(new MemberModel(
                    id,
                    member.Name,
                    memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    isProperty,
                    info));
            }
        }

        // Return null only when there is nothing to do — no members and no diagnostics to report.
        if (members.Count == 0 && diagnostics.Count == 0)
        {
            return null;
        }

        return new TypeModel(
            ns,
            typeSymbol.Name,
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            members.OrderBy(m => m.Id).ToList(),
            typeSymbol.IsValueType,
            diagnostics);
    }

    private static SerializeInfo ResolveSerializeInfo(
        ITypeSymbol type,
        string memberName,
        Location memberLocation,
        List<Diagnostic> diagnostics)
    {
        string fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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

        if (kind != MemberSerializeKind.Fallback) return new SerializeInfo(kind, fq);

        if (type.ToDisplayString() == "System.Guid")
            return new SerializeInfo(MemberSerializeKind.Guid, fq);

        if (type.ToDisplayString() == "System.DateTimeOffset")
            return new SerializeInfo(MemberSerializeKind.DateTimeOffset, fq);

        // Plain single-dimensional arrays (T[]). byte[] is excluded — it already has a
        // dedicated ByteArrayCodec / GrainMessageSerializer.ByteArray fast path that a generic
        // per-element loop would silently replace with a slower, wire-incompatible format.
        // Multi-dimensional / non-zero-based arrays are left unrecognized (Fallback), same as today.
        if (type is IArrayTypeSymbol { IsSZArray: true } arrayType
            && arrayType.ElementType.SpecialType != SpecialType.System_Byte)
        {
            ITypeSymbol elemType = arrayType.ElementType;
            SerializeInfo elemInfo = ResolveSerializeInfo(elemType, memberName, memberLocation, diagnostics);
            if (elemInfo.Kind == MemberSerializeKind.Fallback || IsCollectionKind(elemInfo.Kind))
            {
                diagnostics.Add(Diagnostic.Create(
                    UnsupportedCollectionElementType,
                    memberLocation,
                    memberName,
                    elemType.ToDisplayString()));
                return new SerializeInfo(MemberSerializeKind.Invalid, fq);
            }

            return new SerializeInfo(MemberSerializeKind.Array, fq, element: elemInfo);
        }

        if (type is INamedTypeSymbol named)
        {
            // [GenerateSerializer] nested type
            foreach (AttributeData attr in type.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == GenerateSerializerFqn)
                {
                    string nsPrefix = named.ContainingNamespace.IsGlobalNamespace
                        ? "global::"
                        : $"global::{named.ContainingNamespace.ToDisplayString()}.";
                    return new SerializeInfo(MemberSerializeKind.GeneratedCodec, fq,
                        copierFqTypeName: $"{nsPrefix}{named.Name}Copier");
                }
            }

            string containingNs = named.ContainingNamespace.ToDisplayString();
            string metaName = named.OriginalDefinition.MetadataName;

            // Sequence-shaped collections: System.Collections.Immutable's Array/List/HashSet/
            // SortedSet/Stack/Queue, plus the mutable System.Collections.Generic.List<T>.
            MemberSerializeKind? sequenceKind = (containingNs, metaName) switch
            {
                ("System.Collections.Immutable", "ImmutableArray`1")     => MemberSerializeKind.ImmutableArray,
                ("System.Collections.Immutable", "ImmutableList`1")      => MemberSerializeKind.ImmutableList,
                ("System.Collections.Immutable", "ImmutableHashSet`1")   => MemberSerializeKind.ImmutableHashSet,
                ("System.Collections.Immutable", "ImmutableSortedSet`1") => MemberSerializeKind.ImmutableSortedSet,
                ("System.Collections.Immutable", "ImmutableStack`1")     => MemberSerializeKind.ImmutableStack,
                ("System.Collections.Immutable", "ImmutableQueue`1")     => MemberSerializeKind.ImmutableQueue,
                ("System.Collections.Generic", "List`1")                 => MemberSerializeKind.List,
                _                                                        => null
            };

            if (sequenceKind is { } seqKind)
            {
                ITypeSymbol elemType = named.TypeArguments[0];
                SerializeInfo elemInfo = ResolveSerializeInfo(elemType, memberName, memberLocation, diagnostics);
                // Collection-of-collection elements aren't supported yet — the static-path
                // emitter reuses the same loop-variable name at every nesting depth (a
                // two-level-deep member would emit `foreach (var _item in _item)`), and the
                // field-codec path has no registered IFieldCodec<T> for a raw collection
                // element type. Reject at generate time rather than emit broken/throwing code.
                if (elemInfo.Kind == MemberSerializeKind.Fallback || IsCollectionKind(elemInfo.Kind))
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedCollectionElementType,
                        memberLocation,
                        memberName,
                        elemType.ToDisplayString()));
                    return new SerializeInfo(MemberSerializeKind.Invalid, fq);
                }

                return new SerializeInfo(seqKind, fq, element: elemInfo);
            }

            // Map-shaped collections: Immutable{Sorted}Dictionary plus mutable Dictionary<K,V>.
            MemberSerializeKind? mapKind = (containingNs, metaName) switch
            {
                ("System.Collections.Immutable", "ImmutableDictionary`2")       => MemberSerializeKind.ImmutableDictionary,
                ("System.Collections.Immutable", "ImmutableSortedDictionary`2") => MemberSerializeKind.ImmutableSortedDictionary,
                ("System.Collections.Generic", "Dictionary`2")                  => MemberSerializeKind.Dictionary,
                _                                                               => null
            };

            if (mapKind is { } mpKind)
            {
                ITypeSymbol keyType = named.TypeArguments[0];
                ITypeSymbol valType = named.TypeArguments[1];

                SerializeInfo keyInfo = ResolveSerializeInfo(keyType, memberName, memberLocation, diagnostics);
                if (keyInfo.Kind == MemberSerializeKind.Fallback || IsCollectionKind(keyInfo.Kind))
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedCollectionElementType,
                        memberLocation,
                        memberName,
                        keyType.ToDisplayString()));
                    return new SerializeInfo(MemberSerializeKind.Invalid, fq);
                }

                // Collection-of-collection values aren't supported yet — see the sequence-shape
                // branch above for why (loop-variable collision on the static path, no
                // registered codec for a raw collection value on the field-codec path).
                SerializeInfo valInfo = ResolveSerializeInfo(valType, memberName, memberLocation, diagnostics);
                if (valInfo.Kind == MemberSerializeKind.Fallback || IsCollectionKind(valInfo.Kind))
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedCollectionElementType,
                        memberLocation,
                        memberName,
                        valType.ToDisplayString()));
                    return new SerializeInfo(MemberSerializeKind.Invalid, fq);
                }

                return new SerializeInfo(mpKind, fq, key: keyInfo, element: valInfo);
            }
        }

        // Enum types — write/read as their underlying integral type with a cast.
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumNamedType)
        {
            MemberSerializeKind underlyingKind = (enumNamedType.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32) switch
            {
                SpecialType.System_Byte    => MemberSerializeKind.UInt8,
                SpecialType.System_SByte   => MemberSerializeKind.Int8,
                SpecialType.System_Int16   => MemberSerializeKind.Int16,
                SpecialType.System_UInt16  => MemberSerializeKind.UInt16,
                SpecialType.System_Int32   => MemberSerializeKind.Int32,
                SpecialType.System_UInt32  => MemberSerializeKind.UInt32,
                SpecialType.System_Int64   => MemberSerializeKind.Int64,
                SpecialType.System_UInt64  => MemberSerializeKind.UInt64,
                _                          => MemberSerializeKind.Int32
            };
            return new SerializeInfo(MemberSerializeKind.Enum, fq, underlyingKind: underlyingKind);
        }

        return new SerializeInfo(MemberSerializeKind.Fallback, fq);
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
            foreach (Diagnostic d in model.Diagnostics)
            {
                ctx.ReportDiagnostic(d);
            }

            if (model.Members.Count == 0)
            {
                continue;
            }

            string source = BuildCodecSource(model);
            ctx.AddSource(
                $"{model.TypeName}.QuarkSerializer.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }
    }

    private static bool IsCollectionKind(MemberSerializeKind kind) =>
        IsImmutableCollectionKind(kind) || IsMutableCollectionKind(kind);

    // Safe to share by reference in DeepCopy/CloneStatic — the collection itself can't be mutated.
    private static bool IsImmutableCollectionKind(MemberSerializeKind kind) =>
        kind is MemberSerializeKind.ImmutableArray
            or MemberSerializeKind.ImmutableList
            or MemberSerializeKind.ImmutableHashSet
            or MemberSerializeKind.ImmutableSortedSet
            or MemberSerializeKind.ImmutableDictionary
            or MemberSerializeKind.ImmutableSortedDictionary
            or MemberSerializeKind.ImmutableStack
            or MemberSerializeKind.ImmutableQueue;

    // Mutable — DeepCopy/CloneStatic must allocate a new container rather than share the reference.
    private static bool IsMutableCollectionKind(MemberSerializeKind kind) =>
        kind is MemberSerializeKind.List
            or MemberSerializeKind.Dictionary
            or MemberSerializeKind.Array;

    private static bool IsMapKind(MemberSerializeKind kind) =>
        kind is MemberSerializeKind.ImmutableDictionary
            or MemberSerializeKind.ImmutableSortedDictionary
            or MemberSerializeKind.Dictionary;

    // ImmutableArray uses `.IsDefault`; every other reference-type collection uses a null check.
    private static bool UsesIsDefaultCheck(MemberSerializeKind kind) =>
        kind == MemberSerializeKind.ImmutableArray;

    // New-container clone for mutable collection members: allocates a fresh List/Dictionary/
    // array so the clone and the original don't alias the same mutable instance, but shares
    // element references (matches GrainProxyGenerator's CloneKind.NewList/NewArray/NewDictionary
    // convention used for top-level grain-call argument isolation).
    private static string EmitNewContainerCloneExpr(MemberModel member, string srcExpr)
    {
        SerializeInfo info = member.Info;
        return member.SerializeKind switch
        {
            MemberSerializeKind.List =>
                $"{srcExpr} is null ? default! : new global::System.Collections.Generic.List<{info.Element!.FqTypeName}>({srcExpr})",
            // Avoid a `.ToArray()` LINQ call — the generated file has no `using System.Linq;`
            // and must not depend on the consumer project enabling ImplicitUsings.
            MemberSerializeKind.Array =>
                $"{srcExpr} is null ? default! : ({info.Element!.FqTypeName}[]){srcExpr}.Clone()",
            MemberSerializeKind.Dictionary =>
                $"{srcExpr} is null ? default! : new global::System.Collections.Generic.Dictionary<{info.Key!.FqTypeName}, {info.Element!.FqTypeName}>({srcExpr})",
            _ => throw new InvalidOperationException($"Unhandled mutable collection kind {member.SerializeKind}"),
        };
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
            EmitFieldCodecMemberWrite(sb, member);
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
            EmitFieldCodecMemberRead(sb, member);
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
            if (IsImmutableCollectionKind(member.SerializeKind))
            {
                // Immutable collections are safe to share by reference — identity copy.
                sb.AppendLine($"        copy.{member.Name} = input.{member.Name};");
            }
            else if (IsMutableCollectionKind(member.SerializeKind))
            {
                sb.AppendLine($"        copy.{member.Name} = {EmitNewContainerCloneExpr(member, $"input.{member.Name}")};");
            }
            else
            {
                sb.AppendLine(
                    $"        copy.{member.Name} = _copiers.GetRequiredCopier<{member.FqTypeName}>().DeepCopy(input.{member.Name}, context);");
            }
        }

        sb.AppendLine("        return copy;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CloneStatic
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
            // Mutable collections need a new container (List/Dictionary/array element refs
            // still shared) — matching GrainProxyGenerator's CloneKind.NewList/NewArray/
            // NewDictionary convention for grain-call argument isolation. Everything else
            // (scalars, immutable collections, nested [GenerateSerializer] members) keeps the
            // existing blanket shallow copy.
            string expr = IsMutableCollectionKind(member.SerializeKind)
                ? EmitNewContainerCloneExpr(member, $"input.{member.Name}")
                : $"input.{member.Name}";
            sb.AppendLine($"            {member.Name} = {expr},");
        }
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // WriteStatic / ReadStatic
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

        // Emit ReadCollection_* helpers for collection members
        foreach (MemberModel member in m.Members)
        {
            if (IsCollectionKind(member.SerializeKind))
            {
                sb.AppendLine();
                EmitReadCollectionHelper(sb, member);
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Field-codec path emission
    // -----------------------------------------------------------------------

    private static void EmitFieldCodecMemberWrite(StringBuilder sb, MemberModel member)
    {
        if (!IsCollectionKind(member.SerializeKind))
        {
            sb.AppendLine(
                $"        _codecs.GetRequiredCodec<{member.FqTypeName}>().WriteField(writer, {member.Id}u, typeof({member.FqTypeName}), value.{member.Name});");
            return;
        }

        string val = $"value.{member.Name}";
        string id = $"{member.Id}u";
        SerializeInfo info = member.Info;
        string emptyCheck = UsesIsDefaultCheck(member.SerializeKind) ? $"{val}.IsDefault" : $"{val} is null";

        if (!IsMapKind(member.SerializeKind))
        {
            // Every sequence kind (Array/List/Stack/Queue/ImmutableList/HashSet/SortedSet/
            // ImmutableArray) writes identically here: enumeration order only matters on
            // read, where per-kind reconstruction (see EmitFieldCodecMemberRead) applies.
            sb.AppendLine($"        if ({emptyCheck})");
            sb.AppendLine("        {");
            sb.AppendLine($"            writer.WriteFieldHeader({id}, global::Quark.Serialization.Abstractions.Buffers.WireType.Extended);");
            sb.AppendLine("            writer.WriteByte((byte)global::Quark.Serialization.Abstractions.Buffers.ExtendedWireType.Null);");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine($"            writer.WriteFieldHeader({id}, global::Quark.Serialization.Abstractions.Buffers.WireType.TagDelimited);");
            sb.AppendLine($"            foreach (var _item in {val})");
            sb.AppendLine($"                _codecs.GetRequiredCodec<{info.Element!.FqTypeName}>().WriteField(writer, 1u, typeof({info.Element!.FqTypeName}), _item);");
            sb.AppendLine("            writer.WriteFieldHeader(0u, global::Quark.Serialization.Abstractions.Buffers.WireType.EndTagDelimited);");
            sb.AppendLine("        }");
        }
        else
        {
            // ImmutableDictionary / ImmutableSortedDictionary / Dictionary
            string keyFq = info.Key!.FqTypeName;
            string valFq = info.Element!.FqTypeName;
            sb.AppendLine($"        if ({emptyCheck})");
            sb.AppendLine("        {");
            sb.AppendLine($"            writer.WriteFieldHeader({id}, global::Quark.Serialization.Abstractions.Buffers.WireType.Extended);");
            sb.AppendLine("            writer.WriteByte((byte)global::Quark.Serialization.Abstractions.Buffers.ExtendedWireType.Null);");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine($"            writer.WriteFieldHeader({id}, global::Quark.Serialization.Abstractions.Buffers.WireType.TagDelimited);");
            sb.AppendLine($"            foreach (var _kvp in {val})");
            sb.AppendLine("            {");
            sb.AppendLine($"                _codecs.GetRequiredCodec<{keyFq}>().WriteField(writer, 1u, typeof({keyFq}), _kvp.Key);");
            sb.AppendLine($"                _codecs.GetRequiredCodec<{valFq}>().WriteField(writer, 2u, typeof({valFq}), _kvp.Value);");
            sb.AppendLine("            }");
            sb.AppendLine("            writer.WriteFieldHeader(0u, global::Quark.Serialization.Abstractions.Buffers.WireType.EndTagDelimited);");
            sb.AppendLine("        }");
        }
    }

    private static void EmitFieldCodecMemberRead(StringBuilder sb, MemberModel member)
    {
        if (!IsCollectionKind(member.SerializeKind))
        {
            sb.AppendLine(
                $"                case {member.Id}: result.{member.Name} = _codecs.GetRequiredCodec<{member.FqTypeName}>().ReadValue(reader, f); break;");
            return;
        }

        SerializeInfo info = member.Info;
        sb.AppendLine($"                case {member.Id}:");
        sb.AppendLine("                {");
        sb.AppendLine("                    if (f.WireType == global::Quark.Serialization.Abstractions.Buffers.WireType.Extended) break;");

        if (member.SerializeKind == MemberSerializeKind.ImmutableArray)
        {
            string elemFq = info.Element!.FqTypeName;
            sb.AppendLine($"                    var _b_{member.Name} = global::System.Collections.Immutable.ImmutableArray.CreateBuilder<{elemFq}>();");
            sb.AppendLine("                    global::Quark.Serialization.Abstractions.Buffers.Field _ef;");
            sb.AppendLine($"                    while (!(_ef = reader.ReadFieldHeader()).IsEndObject)");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        if (_ef.FieldId == 1u) _b_{member.Name}.Add(_codecs.GetRequiredCodec<{elemFq}>().ReadValue(reader, _ef));");
            sb.AppendLine("                        else SkipField(reader, _ef);");
            sb.AppendLine("                    }");
            sb.AppendLine($"                    result.{member.Name} = _b_{member.Name}.ToImmutable();");
        }
        else if (member.SerializeKind == MemberSerializeKind.ImmutableStack)
        {
            // ImmutableStack enumerates top-to-bottom (LIFO). Reconstructing by pushing in
            // wire order would reverse the stack, so buffer then push back-to-front — the
            // last item read (original bottom) gets pushed first, the first item read
            // (original top) gets pushed last and ends up on top again.
            string elemFq = info.Element!.FqTypeName;
            sb.AppendLine($"                    var _t_{member.Name} = new global::System.Collections.Generic.List<{elemFq}>();");
            sb.AppendLine("                    global::Quark.Serialization.Abstractions.Buffers.Field _ef;");
            sb.AppendLine($"                    while (!(_ef = reader.ReadFieldHeader()).IsEndObject)");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        if (_ef.FieldId == 1u) _t_{member.Name}.Add(_codecs.GetRequiredCodec<{elemFq}>().ReadValue(reader, _ef));");
            sb.AppendLine("                        else SkipField(reader, _ef);");
            sb.AppendLine("                    }");
            sb.AppendLine($"                    var _s_{member.Name} = global::System.Collections.Immutable.ImmutableStack<{elemFq}>.Empty;");
            sb.AppendLine($"                    for (int _i = _t_{member.Name}.Count - 1; _i >= 0; _i--) _s_{member.Name} = _s_{member.Name}.Push(_t_{member.Name}[_i]);");
            sb.AppendLine($"                    result.{member.Name} = _s_{member.Name};");
        }
        else if (member.SerializeKind == MemberSerializeKind.ImmutableQueue)
        {
            // ImmutableQueue enumerates FIFO — dequeue order matches enqueue order, so
            // enqueuing in wire order reproduces the original queue directly.
            string elemFq = info.Element!.FqTypeName;
            sb.AppendLine($"                    var _q_{member.Name} = global::System.Collections.Immutable.ImmutableQueue<{elemFq}>.Empty;");
            sb.AppendLine("                    global::Quark.Serialization.Abstractions.Buffers.Field _ef;");
            sb.AppendLine($"                    while (!(_ef = reader.ReadFieldHeader()).IsEndObject)");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        if (_ef.FieldId == 1u) _q_{member.Name} = _q_{member.Name}.Enqueue(_codecs.GetRequiredCodec<{elemFq}>().ReadValue(reader, _ef));");
            sb.AppendLine("                        else SkipField(reader, _ef);");
            sb.AppendLine("                    }");
            sb.AppendLine($"                    result.{member.Name} = _q_{member.Name};");
        }
        else if (!IsMapKind(member.SerializeKind))
        {
            string elemFq = info.Element!.FqTypeName;
            string builderNew = member.SerializeKind switch
            {
                MemberSerializeKind.ImmutableList      => $"global::System.Collections.Immutable.ImmutableList.CreateBuilder<{elemFq}>()",
                MemberSerializeKind.ImmutableHashSet   => $"global::System.Collections.Immutable.ImmutableHashSet.CreateBuilder<{elemFq}>()",
                MemberSerializeKind.ImmutableSortedSet => $"global::System.Collections.Immutable.ImmutableSortedSet.CreateBuilder<{elemFq}>()",
                MemberSerializeKind.List
                    or MemberSerializeKind.Array       => $"new global::System.Collections.Generic.List<{elemFq}>()",
                _                                      => throw new InvalidOperationException($"Unhandled sequence kind {member.SerializeKind}"),
            };
            sb.AppendLine($"                    var _b_{member.Name} = {builderNew};");
            sb.AppendLine("                    global::Quark.Serialization.Abstractions.Buffers.Field _ef;");
            sb.AppendLine($"                    while (!(_ef = reader.ReadFieldHeader()).IsEndObject)");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        if (_ef.FieldId == 1u) _b_{member.Name}.Add(_codecs.GetRequiredCodec<{elemFq}>().ReadValue(reader, _ef));");
            sb.AppendLine("                        else SkipField(reader, _ef);");
            sb.AppendLine("                    }");
            string finalize = member.SerializeKind switch
            {
                MemberSerializeKind.List  => $"_b_{member.Name}",
                MemberSerializeKind.Array => $"_b_{member.Name}.ToArray()",
                _                         => $"_b_{member.Name}.ToImmutable()",
            };
            sb.AppendLine($"                    result.{member.Name} = {finalize};");
        }
        else
        {
            string keyFq = info.Key!.FqTypeName;
            string valFq = info.Element!.FqTypeName;
            string builderNew = member.SerializeKind switch
            {
                MemberSerializeKind.ImmutableDictionary       => $"global::System.Collections.Immutable.ImmutableDictionary.CreateBuilder<{keyFq}, {valFq}>()",
                MemberSerializeKind.ImmutableSortedDictionary => $"global::System.Collections.Immutable.ImmutableSortedDictionary.CreateBuilder<{keyFq}, {valFq}>()",
                MemberSerializeKind.Dictionary                 => $"new global::System.Collections.Generic.Dictionary<{keyFq}, {valFq}>()",
                _                                              => throw new InvalidOperationException($"Unhandled map kind {member.SerializeKind}"),
            };
            sb.AppendLine($"                    var _b_{member.Name} = {builderNew};");
            sb.AppendLine("                    global::Quark.Serialization.Abstractions.Buffers.Field _kf;");
            sb.AppendLine($"                    while (!(_kf = reader.ReadFieldHeader()).IsEndObject)");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        var _k = _codecs.GetRequiredCodec<{keyFq}>().ReadValue(reader, _kf);");
            sb.AppendLine("                        var _vf = reader.ReadFieldHeader();");
            sb.AppendLine($"                        var _v = _codecs.GetRequiredCodec<{valFq}>().ReadValue(reader, _vf);");
            sb.AppendLine($"                        _b_{member.Name}.Add(_k, _v);");
            sb.AppendLine("                    }");
            string finalize = member.SerializeKind == MemberSerializeKind.Dictionary
                ? $"_b_{member.Name}"
                : $"_b_{member.Name}.ToImmutable()";
            sb.AppendLine($"                    result.{member.Name} = {finalize};");
        }

        sb.AppendLine("                    break;");
        sb.AppendLine("                }");
    }

    // -----------------------------------------------------------------------
    // Static transport path emission
    // -----------------------------------------------------------------------

    private static void EmitMemberWrite(StringBuilder sb, MemberModel member)
    {
        EmitValueWrite(sb, "        ", $"value.{member.Name}", member.Info);
    }

    private static void EmitValueWrite(StringBuilder sb, string indent, string valueExpr, SerializeInfo info)
    {
        switch (info.Kind)
        {
            case MemberSerializeKind.Bool:
                sb.AppendLine($"{indent}writer.WriteByte({valueExpr} ? (byte)1 : (byte)0);");
                break;
            case MemberSerializeKind.UInt8:
                sb.AppendLine($"{indent}writer.WriteByte((byte){valueExpr});");
                break;
            case MemberSerializeKind.Int8:
                sb.AppendLine($"{indent}writer.WriteInt32((int){valueExpr});");
                break;
            case MemberSerializeKind.Int16:
                sb.AppendLine($"{indent}writer.WriteInt32((int){valueExpr});");
                break;
            case MemberSerializeKind.UInt16:
                sb.AppendLine($"{indent}writer.WriteVarUInt32((uint){valueExpr});");
                break;
            case MemberSerializeKind.Int32:
                sb.AppendLine($"{indent}writer.WriteInt32({valueExpr});");
                break;
            case MemberSerializeKind.UInt32:
                sb.AppendLine($"{indent}writer.WriteVarUInt32({valueExpr});");
                break;
            case MemberSerializeKind.Int64:
                sb.AppendLine($"{indent}writer.WriteInt64({valueExpr});");
                break;
            case MemberSerializeKind.UInt64:
                sb.AppendLine($"{indent}writer.WriteVarUInt64({valueExpr});");
                break;
            case MemberSerializeKind.Float:
                sb.AppendLine($"{indent}writer.WriteFixed32(unchecked((uint)global::System.BitConverter.SingleToInt32Bits({valueExpr})));");
                break;
            case MemberSerializeKind.Double:
                sb.AppendLine($"{indent}writer.WriteFixed64(unchecked((ulong)global::System.BitConverter.DoubleToInt64Bits({valueExpr})));");
                break;
            case MemberSerializeKind.String:
                sb.AppendLine($"{indent}writer.WriteString({valueExpr});");
                break;
            case MemberSerializeKind.Guid:
                sb.AppendLine($"{indent}writer.WriteRaw({valueExpr}.ToByteArray());");
                break;
            case MemberSerializeKind.DateTimeOffset:
                sb.AppendLine($"{indent}writer.WriteInt64({valueExpr}.Ticks); writer.WriteInt64({valueExpr}.Offset.Ticks);");
                break;
            case MemberSerializeKind.GeneratedCodec:
                sb.AppendLine($"{indent}{info.CopierFqTypeName}.WriteStatic(ref writer, {valueExpr});");
                break;
            case MemberSerializeKind.Enum:
                string enumWrite = info.UnderlyingKind switch
                {
                    MemberSerializeKind.UInt8  => $"writer.WriteByte((byte){valueExpr});",
                    MemberSerializeKind.Int8   => $"writer.WriteInt32((int)(sbyte){valueExpr});",
                    MemberSerializeKind.Int16  => $"writer.WriteInt32((int)(short){valueExpr});",
                    MemberSerializeKind.UInt16 => $"writer.WriteVarUInt32((uint)(ushort){valueExpr});",
                    MemberSerializeKind.Int32  => $"writer.WriteInt32((int){valueExpr});",
                    MemberSerializeKind.UInt32 => $"writer.WriteVarUInt32((uint){valueExpr});",
                    MemberSerializeKind.Int64  => $"writer.WriteInt64((long){valueExpr});",
                    MemberSerializeKind.UInt64 => $"writer.WriteVarUInt64((ulong){valueExpr});",
                    _                          => $"writer.WriteInt32((int){valueExpr});"
                };
                sb.AppendLine($"{indent}{enumWrite}");
                break;
            case MemberSerializeKind.ImmutableArray:
            {
                string elemFq = info.Element!.FqTypeName;
                sb.AppendLine($"{indent}if ({valueExpr}.IsDefault) {{ writer.WriteByte(0); }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    writer.WriteByte(1);");
                sb.AppendLine($"{indent}    writer.WriteVarUInt32((uint){valueExpr}.Length);");
                sb.AppendLine($"{indent}    foreach (var _item in {valueExpr})");
                sb.AppendLine($"{indent}    {{");
                EmitValueWrite(sb, indent + "        ", "_item", info.Element!);
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine($"{indent}}}");
                break;
            }
            case MemberSerializeKind.ImmutableList:
            case MemberSerializeKind.ImmutableHashSet:
            case MemberSerializeKind.ImmutableSortedSet:
            case MemberSerializeKind.List:
            {
                sb.AppendLine($"{indent}if ({valueExpr} is null) {{ writer.WriteByte(0); }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    writer.WriteByte(1);");
                sb.AppendLine($"{indent}    writer.WriteVarUInt32((uint){valueExpr}.Count);");
                sb.AppendLine($"{indent}    foreach (var _item in {valueExpr})");
                sb.AppendLine($"{indent}    {{");
                EmitValueWrite(sb, indent + "        ", "_item", info.Element!);
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine($"{indent}}}");
                break;
            }
            case MemberSerializeKind.Array:
            {
                sb.AppendLine($"{indent}if ({valueExpr} is null) {{ writer.WriteByte(0); }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    writer.WriteByte(1);");
                sb.AppendLine($"{indent}    writer.WriteVarUInt32((uint){valueExpr}.Length);");
                sb.AppendLine($"{indent}    foreach (var _item in {valueExpr})");
                sb.AppendLine($"{indent}    {{");
                EmitValueWrite(sb, indent + "        ", "_item", info.Element!);
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine($"{indent}}}");
                break;
            }
            case MemberSerializeKind.ImmutableStack:
            case MemberSerializeKind.ImmutableQueue:
            {
                // Neither ImmutableStack<T> nor ImmutableQueue<T> exposes an O(1) Count, so
                // count via a first pass before writing — cheap since these are immutable
                // (no risk of the two enumerations observing different contents).
                sb.AppendLine($"{indent}if ({valueExpr} is null) {{ writer.WriteByte(0); }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    writer.WriteByte(1);");
                sb.AppendLine($"{indent}    int _count = 0;");
                sb.AppendLine($"{indent}    foreach (var _item in {valueExpr}) _count++;");
                sb.AppendLine($"{indent}    writer.WriteVarUInt32((uint)_count);");
                sb.AppendLine($"{indent}    foreach (var _item in {valueExpr})");
                sb.AppendLine($"{indent}    {{");
                EmitValueWrite(sb, indent + "        ", "_item", info.Element!);
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine($"{indent}}}");
                break;
            }
            case MemberSerializeKind.ImmutableDictionary:
            case MemberSerializeKind.ImmutableSortedDictionary:
            case MemberSerializeKind.Dictionary:
            {
                sb.AppendLine($"{indent}if ({valueExpr} is null) {{ writer.WriteByte(0); }}");
                sb.AppendLine($"{indent}else");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    writer.WriteByte(1);");
                sb.AppendLine($"{indent}    writer.WriteVarUInt32((uint){valueExpr}.Count);");
                sb.AppendLine($"{indent}    foreach (var _kvp in {valueExpr})");
                sb.AppendLine($"{indent}    {{");
                EmitValueWrite(sb, indent + "        ", "_kvp.Key", info.Key!);
                EmitValueWrite(sb, indent + "        ", "_kvp.Value", info.Element!);
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine($"{indent}}}");
                break;
            }
            default:
                sb.AppendLine($"{indent}global::Quark.Runtime.GrainMessageSerializer.WriteValue(writer, {valueExpr});");
                break;
        }
    }

    private static void EmitMemberRead(StringBuilder sb, MemberModel member)
    {
        if (IsCollectionKind(member.SerializeKind))
        {
            sb.AppendLine($"            {member.Name} = ReadCollection_{member.Name}(ref reader),");
            return;
        }

        string readExpr = EmitValueReadExpr(member.Info);
        sb.AppendLine($"            {member.Name} = {readExpr},");
    }

    private static string EmitValueReadExpr(SerializeInfo info) => info.Kind switch
    {
        MemberSerializeKind.Bool            => "reader.ReadByte() != 0",
        MemberSerializeKind.UInt8           => "reader.ReadByte()",
        MemberSerializeKind.Int8
            or MemberSerializeKind.Int16    => $"({info.FqTypeName})reader.ReadInt32()",
        MemberSerializeKind.UInt16          => $"({info.FqTypeName})reader.ReadVarUInt32()",
        MemberSerializeKind.Int32           => "reader.ReadInt32()",
        MemberSerializeKind.UInt32          => "reader.ReadVarUInt32()",
        MemberSerializeKind.Int64           => "reader.ReadInt64()",
        MemberSerializeKind.UInt64          => "reader.ReadVarUInt64()",
        MemberSerializeKind.Float           => "global::System.BitConverter.Int32BitsToSingle(unchecked((int)reader.ReadFixed32()))",
        MemberSerializeKind.Double          => "global::System.BitConverter.Int64BitsToDouble(unchecked((long)reader.ReadFixed64()))",
        MemberSerializeKind.String          => "reader.ReadString()",
        MemberSerializeKind.Guid            => "new global::System.Guid(reader.ReadRaw(16))",
        MemberSerializeKind.DateTimeOffset  => "new global::System.DateTimeOffset(reader.ReadInt64(), global::System.TimeSpan.FromTicks(reader.ReadInt64()))",
        MemberSerializeKind.GeneratedCodec  => $"{info.CopierFqTypeName}.ReadStatic(ref reader)!",
        MemberSerializeKind.Enum            => info.UnderlyingKind switch
        {
            MemberSerializeKind.UInt8  => $"({info.FqTypeName})reader.ReadByte()",
            MemberSerializeKind.Int8   => $"({info.FqTypeName})(sbyte)reader.ReadInt32()",
            MemberSerializeKind.Int16  => $"({info.FqTypeName})(short)reader.ReadInt32()",
            MemberSerializeKind.UInt16 => $"({info.FqTypeName})(ushort)reader.ReadVarUInt32()",
            MemberSerializeKind.Int32  => $"({info.FqTypeName})reader.ReadInt32()",
            MemberSerializeKind.UInt32 => $"({info.FqTypeName})reader.ReadVarUInt32()",
            MemberSerializeKind.Int64  => $"({info.FqTypeName})reader.ReadInt64()",
            MemberSerializeKind.UInt64 => $"({info.FqTypeName})reader.ReadVarUInt64()",
            _                          => $"({info.FqTypeName})reader.ReadInt32()"
        },
        _ => $"({info.FqTypeName})global::Quark.Runtime.GrainMessageSerializer.ReadArg(ref reader)!"
    };

    private static void EmitReadCollectionHelper(StringBuilder sb, MemberModel member)
    {
        SerializeInfo info = member.Info;
        string retType = member.FqTypeName;

        sb.AppendLine($"    private static {retType} ReadCollection_{member.Name}(");
        sb.AppendLine("        ref global::Quark.Serialization.Abstractions.Buffers.CodecReader reader)");
        sb.AppendLine("    {");

        switch (info.Kind)
        {
            case MemberSerializeKind.ImmutableArray:
            {
                string elemFq = info.Element!.FqTypeName;
                string elemRead = EmitValueReadExpr(info.Element!);
                sb.AppendLine("        if (reader.ReadByte() == 0) return default;");
                sb.AppendLine("        uint _count = reader.ReadVarUInt32();");
                sb.AppendLine($"        var _builder = global::System.Collections.Immutable.ImmutableArray.CreateBuilder<{elemFq}>((int)_count);");
                sb.AppendLine($"        for (uint _i = 0; _i < _count; _i++) _builder.Add({elemRead});");
                sb.AppendLine("        return _builder.MoveToImmutable();");
                break;
            }
            case MemberSerializeKind.ImmutableList:
            case MemberSerializeKind.ImmutableHashSet:
            case MemberSerializeKind.ImmutableSortedSet:
            case MemberSerializeKind.List:
            {
                string elemFq = info.Element!.FqTypeName;
                string elemRead = EmitValueReadExpr(info.Element!);
                if (info.Kind == MemberSerializeKind.List)
                {
                    sb.AppendLine("        if (reader.ReadByte() == 0) return default!;");
                    sb.AppendLine("        uint _count = reader.ReadVarUInt32();");
                    sb.AppendLine($"        var _result = new global::System.Collections.Generic.List<{elemFq}>((int)_count);");
                    sb.AppendLine($"        for (uint _i = 0; _i < _count; _i++) _result.Add({elemRead});");
                    sb.AppendLine("        return _result;");
                    break;
                }

                string builderExpr = info.Kind switch
                {
                    MemberSerializeKind.ImmutableList    => $"global::System.Collections.Immutable.ImmutableList.CreateBuilder<{elemFq}>()",
                    MemberSerializeKind.ImmutableHashSet => $"global::System.Collections.Immutable.ImmutableHashSet.CreateBuilder<{elemFq}>()",
                    _                                    => $"global::System.Collections.Immutable.ImmutableSortedSet.CreateBuilder<{elemFq}>()",
                };
                sb.AppendLine("        if (reader.ReadByte() == 0) return default!;");
                sb.AppendLine("        uint _count = reader.ReadVarUInt32();");
                sb.AppendLine($"        var _builder = {builderExpr};");
                sb.AppendLine($"        for (uint _i = 0; _i < _count; _i++) _builder.Add({elemRead});");
                sb.AppendLine("        return _builder.ToImmutable();");
                break;
            }
            case MemberSerializeKind.Array:
            {
                string elemFq = info.Element!.FqTypeName;
                string elemRead = EmitValueReadExpr(info.Element!);
                sb.AppendLine("        if (reader.ReadByte() == 0) return default!;");
                sb.AppendLine("        uint _count = reader.ReadVarUInt32();");
                sb.AppendLine($"        var _result = new {elemFq}[_count];");
                sb.AppendLine($"        for (uint _i = 0; _i < _count; _i++) _result[_i] = {elemRead};");
                sb.AppendLine("        return _result;");
                break;
            }
            case MemberSerializeKind.ImmutableStack:
            {
                // See EmitFieldCodecMemberRead's ImmutableStack branch for why a naive
                // sequential Push reverses the stack — buffer then push back-to-front.
                string elemFq = info.Element!.FqTypeName;
                string elemRead = EmitValueReadExpr(info.Element!);
                sb.AppendLine("        if (reader.ReadByte() == 0) return default!;");
                sb.AppendLine("        uint _count = reader.ReadVarUInt32();");
                sb.AppendLine($"        var _tmp = new {elemFq}[_count];");
                sb.AppendLine($"        for (uint _i = 0; _i < _count; _i++) _tmp[_i] = {elemRead};");
                sb.AppendLine($"        var _result = global::System.Collections.Immutable.ImmutableStack<{elemFq}>.Empty;");
                sb.AppendLine("        for (int _i = (int)_count - 1; _i >= 0; _i--) _result = _result.Push(_tmp[_i]);");
                sb.AppendLine("        return _result;");
                break;
            }
            case MemberSerializeKind.ImmutableQueue:
            {
                string elemFq = info.Element!.FqTypeName;
                string elemRead = EmitValueReadExpr(info.Element!);
                sb.AppendLine("        if (reader.ReadByte() == 0) return default!;");
                sb.AppendLine("        uint _count = reader.ReadVarUInt32();");
                sb.AppendLine($"        var _result = global::System.Collections.Immutable.ImmutableQueue<{elemFq}>.Empty;");
                sb.AppendLine($"        for (uint _i = 0; _i < _count; _i++) _result = _result.Enqueue({elemRead});");
                sb.AppendLine("        return _result;");
                break;
            }
            case MemberSerializeKind.ImmutableDictionary:
            case MemberSerializeKind.ImmutableSortedDictionary:
            case MemberSerializeKind.Dictionary:
            {
                string keyFq = info.Key!.FqTypeName;
                string valFq = info.Element!.FqTypeName;
                string keyRead = EmitValueReadExpr(info.Key!);
                string valRead = EmitValueReadExpr(info.Element!);
                if (info.Kind == MemberSerializeKind.Dictionary)
                {
                    sb.AppendLine("        if (reader.ReadByte() == 0) return default!;");
                    sb.AppendLine("        uint _count = reader.ReadVarUInt32();");
                    sb.AppendLine($"        var _result = new global::System.Collections.Generic.Dictionary<{keyFq}, {valFq}>((int)_count);");
                    sb.AppendLine("        for (uint _i = 0; _i < _count; _i++)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            var _k = {keyRead};");
                    sb.AppendLine($"            var _v = {valRead};");
                    sb.AppendLine("            _result.Add(_k, _v);");
                    sb.AppendLine("        }");
                    sb.AppendLine("        return _result;");
                    break;
                }

                string builderExpr = info.Kind == MemberSerializeKind.ImmutableDictionary
                    ? $"global::System.Collections.Immutable.ImmutableDictionary.CreateBuilder<{keyFq}, {valFq}>()"
                    : $"global::System.Collections.Immutable.ImmutableSortedDictionary.CreateBuilder<{keyFq}, {valFq}>()";
                sb.AppendLine("        if (reader.ReadByte() == 0) return default!;");
                sb.AppendLine("        uint _count = reader.ReadVarUInt32();");
                sb.AppendLine($"        var _builder = {builderExpr};");
                sb.AppendLine("        for (uint _i = 0; _i < _count; _i++)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var _k = {keyRead};");
                sb.AppendLine($"            var _v = {valRead};");
                sb.AppendLine("            _builder.Add(_k, _v);");
                sb.AppendLine("        }");
                sb.AppendLine("        return _builder.ToImmutable();");
                break;
            }
        }

        sb.AppendLine("    }");
    }

    // -----------------------------------------------------------------------
    // Data models (no Roslyn references — safe to cache across incremental steps)
    // -----------------------------------------------------------------------

    private sealed class TypeModel(
        string @namespace,
        string typeName,
        string fqTypeName,
        IReadOnlyList<MemberModel> members,
        bool isValueType,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        public string Namespace { get; } = @namespace;
        public string TypeName { get; } = typeName;
        public string FqTypeName { get; } = fqTypeName;
        public IReadOnlyList<MemberModel> Members { get; } = members;
        public bool IsValueType { get; } = isValueType;
        public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    }

    private enum MemberSerializeKind
    {
        Bool,
        Int8, Int16, Int32, Int64,
        UInt8, UInt16, UInt32, UInt64,
        Float, Double,
        String, Guid, DateTimeOffset,
        GeneratedCodec,
        Enum,
        ImmutableArray, ImmutableList, ImmutableHashSet, ImmutableSortedSet,
        ImmutableDictionary, ImmutableSortedDictionary,
        ImmutableStack, ImmutableQueue,
        List, Dictionary, Array,
        Invalid,
        Fallback
    }

    private sealed class SerializeInfo(
        MemberSerializeKind kind,
        string fqTypeName,
        string? copierFqTypeName = null,
        MemberSerializeKind underlyingKind = MemberSerializeKind.Fallback,
        SerializeInfo? element = null,
        SerializeInfo? key = null)
    {
        public MemberSerializeKind Kind { get; } = kind;
        public string FqTypeName { get; } = fqTypeName;
        public string? CopierFqTypeName { get; } = copierFqTypeName;
        public MemberSerializeKind UnderlyingKind { get; } = underlyingKind;
        public SerializeInfo? Element { get; } = element;
        public SerializeInfo? Key { get; } = key;
    }

    private sealed class MemberModel(
        uint id,
        string name,
        string fqTypeName,
        bool isProperty,
        SerializeInfo info)
    {
        public uint Id { get; } = id;
        public string Name { get; } = name;
        public string FqTypeName { get; } = fqTypeName;
        public bool IsProperty { get; } = isProperty;
        public SerializeInfo Info { get; } = info;

        // Passthroughs so existing call sites keep compiling.
        public MemberSerializeKind SerializeKind => Info.Kind;
        public string? CopierFqTypeName => Info.CopierFqTypeName;
        public MemberSerializeKind UnderlyingKind => Info.UnderlyingKind;
    }
}
