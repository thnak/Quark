using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Unit.Serialization;

/// <summary>
///     End-to-end coverage for the version-tolerant composite serialization contract that
///     <c>SerializerGenerator</c> emits (issue #31). The codecs/copiers below are hand-written
///     (per CLAUDE.md test guidance) but mirror the generated template byte-for-byte:
///     <list type="bullet">
///         <item><c>WriteField</c> — Extended-null header for null refs, else TagDelimited + each
///         member by ascending <c>[Id]</c> + EndTagDelimited terminator.</item>
///         <item><c>ReadValue</c> — field-id switch dispatch with a <c>default: SkipField</c> arm
///         that recursively skips unknown fields (the forward-compat guarantee).</item>
///         <item><c>DeepCopy</c> — reference-tracked recursive deep copy.</item>
///     </list>
///     This is the first test to actually compile + roundtrip bytes through the contract; the
///     existing CodeGenerator tests only assert generated-source shape.
/// </summary>
public sealed class SerializerRoundtripTests
{
    private readonly QuarkSerializer _serializer;
    private readonly ICopierProvider _copiers;

    public SerializerRoundtripTests()
    {
        ServiceCollection services = new();
        services.AddQuarkSerialization();

        services.AddSingleton<IFieldCodec<Address>>(sp => new AddressCodec(sp.GetRequiredService<ICodecProvider>()));
        services.AddSingleton<IFieldCodec<PersonV1>>(sp => new PersonV1Codec(sp.GetRequiredService<ICodecProvider>()));
        services.AddSingleton<IFieldCodec<PersonV2>>(sp => new PersonV2Codec(sp.GetRequiredService<ICodecProvider>()));

        services.AddSingleton<IDeepCopier<Address>>(sp => new AddressCopier(sp.GetRequiredService<ICopierProvider>()));
        services.AddSingleton<IDeepCopier<PersonV1>>(sp => new PersonV1Copier(sp.GetRequiredService<ICopierProvider>()));
        services.AddSingleton<IDeepCopier<PersonV2>>(sp => new PersonV2Copier(sp.GetRequiredService<ICopierProvider>()));

        ServiceProvider sp = services.BuildServiceProvider();
        _serializer = sp.GetRequiredService<QuarkSerializer>();
        _copiers = sp.GetRequiredService<ICopierProvider>();
    }

    // =====================================================================
    // Basic roundtrip
    // =====================================================================

    [Fact]
    public void Roundtrip_AllMembers_DeepEqual()
    {
        var original = new PersonV2
        {
            Name = "Ada Lovelace",
            Age = 36,
            Email = "ada@analytical.engine",
            Score = 99.5,
            Rating = 4.5f,
            Version = 7,
            Home = new Address { Street = "12 Baker St", Zip = 1837 },
            Nickname = "Countess",
            Tag = "pioneer",
        };

        PersonV2 result = _serializer.Deserialize<PersonV2>(_serializer.SerializeToArray(original))!;

        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Age, result.Age);
        Assert.Equal(original.Email, result.Email);
        Assert.Equal(original.Score, result.Score);
        Assert.Equal(original.Rating, result.Rating);
        Assert.Equal(original.Version, result.Version);
        Assert.Equal(original.Nickname, result.Nickname);
        Assert.Equal(original.Tag, result.Tag);
        Assert.NotNull(result.Home);
        Assert.Equal(original.Home.Street, result.Home!.Street);
        Assert.Equal(original.Home.Zip, result.Home.Zip);
    }

    [Fact]
    public void Roundtrip_NullAndDefaultMembers_Preserved()
    {
        var original = new PersonV1 { Name = null, Age = 0 };

        PersonV1 result = _serializer.Deserialize<PersonV1>(_serializer.SerializeToArray(original))!;

        Assert.Null(result.Name);
        Assert.Equal(0, result.Age);
    }

    // =====================================================================
    // Version tolerance — forward compat (read a newer payload with an older codec)
    // =====================================================================

    [Fact]
    public void ForwardCompat_UnknownScalarFields_AreSkipped_KnownFieldsIntact()
    {
        // A "v2" payload carries extra [Id]s (5..10) the v1 codec has never heard of. Each unknown
        // field exercises a distinct SkipField wire-type arm: LengthPrefixed (Email), Fixed64
        // (Score), Fixed32 (Rating), VarInt (Version), Extended-null (Home + Nickname). All of these
        // sit between Age (Id 2) and Tag (Id 20), so Tag is only readable if every skip stayed in
        // sync — making this assertion cover every scalar/extended SkipField branch at once.
        var v2 = new PersonV2
        {
            Name = "Grace Hopper",
            Age = 85,
            Email = "grace@navy.mil",
            Score = 1.0,
            Rating = 5f,
            Version = 2,
            Home = null,
            Nickname = null,
            Tag = "rear admiral",
        };

        byte[] bytes = _serializer.SerializeToArray(v2);
        PersonV1 v1 = _serializer.Deserialize<PersonV1>(bytes)!;

        Assert.Equal("Grace Hopper", v1.Name);
        Assert.Equal(85, v1.Age);
        Assert.Equal("rear admiral", v1.Tag);
    }

    [Fact]
    public void ForwardCompat_UnknownNestedTagDelimitedField_IsRecursivelySkipped()
    {
        // The Home field (Id 8) is a nested TagDelimited composite. SkipField must recurse through
        // its inner Street/Zip fields up to the inner EndTagDelimited before resuming the outer
        // stream — a desynced skip would corrupt or throw on the trailing known fields.
        var v2 = new PersonV2
        {
            Name = "Katherine Johnson",
            Age = 101,
            Home = new Address { Street = "Hidden Figures Way", Zip = 23681 },
            Nickname = "human computer",
            Tag = "mathematician",
        };

        byte[] bytes = _serializer.SerializeToArray(v2);
        PersonV1 v1 = _serializer.Deserialize<PersonV1>(bytes)!;

        Assert.Equal("Katherine Johnson", v1.Name);
        Assert.Equal(101, v1.Age);
        // Tag (Id 20) sits after the nested Home (Id 8); reading it proves the TagDelimited skip
        // recursed past Home's inner Street/Zip and resumed exactly at the next outer field.
        Assert.Equal("mathematician", v1.Tag);
    }

    [Fact]
    public void ForwardCompat_UnknownExtendedNullField_IsSkipped()
    {
        // A null reference member (Nickname, Id 9) is written as an Extended/Null header carrying
        // no trailing payload. The Extended SkipField arm must consume exactly the header.
        var v2 = new PersonV2
        {
            Name = "Radia Perlman",
            Age = 73,
            Nickname = null, // Extended-null on the wire
            Tag = "internet's mother",
        };

        byte[] bytes = _serializer.SerializeToArray(v2);
        PersonV1 v1 = _serializer.Deserialize<PersonV1>(bytes)!;

        Assert.Equal("Radia Perlman", v1.Name);
        Assert.Equal(73, v1.Age);
        Assert.Equal("internet's mother", v1.Tag);
    }

    // =====================================================================
    // Field ordering — switch dispatch is order-independent
    // =====================================================================

    [Fact]
    public void Deserialize_FieldsInDescendingIdOrder_StillReadsCorrectly()
    {
        // Hand-compose a PersonV1 payload writing Age (Id 2) BEFORE Name (Id 1). The generator
        // always emits ascending order, but the reader dispatches on field id, so a reordered or
        // non-contiguous stream must deserialize identically.
        ArrayBufferWriter<byte> buf = new();
        var writer = new CodecWriter(buf);

        writer.WriteFieldHeader(0u, WireType.TagDelimited);          // root composite header (field id 0)
        writer.WriteFieldHeader(2u, WireType.VarInt);                // Age first
        writer.WriteInt32(42);
        writer.WriteFieldHeader(1u, WireType.LengthPrefixed);        // Name second
        writer.WriteString("out-of-order");
        writer.WriteFieldHeader(0u, WireType.EndTagDelimited);       // terminator

        PersonV1 result = _serializer.Deserialize<PersonV1>(buf.WrittenMemory.ToArray())!;

        Assert.Equal("out-of-order", result.Name);
        Assert.Equal(42, result.Age);
    }

    // =====================================================================
    // Deep-copier correctness
    // =====================================================================

    [Fact]
    public void DeepCopy_NestedReference_IsIndependentOfSource()
    {
        var original = new PersonV2
        {
            Name = "Barbara Liskov",
            Age = 85,
            Home = new Address { Street = "MIT", Zip = 2139 },
        };

        IDeepCopier<PersonV2> copier = _copiers.GetRequiredCopier<PersonV2>();
        PersonV2 copy = copier.DeepCopy(original, new CopyContext());

        // A nested reference must be cloned, not shared.
        Assert.NotSame(original, copy);
        Assert.NotSame(original.Home, copy.Home);
        Assert.Equal(original.Home!.Street, copy.Home!.Street);

        // Mutating the copy's nested object must not bleed back into the source.
        copy.Home.Street = "mutated";
        copy.Home.Zip = 0;
        copy.Name = "mutated";

        Assert.Equal("MIT", original.Home.Street);
        Assert.Equal(2139, original.Home.Zip);
        Assert.Equal("Barbara Liskov", original.Name);
    }

    [Fact]
    public void DeepCopy_NullMember_RoundTrips()
    {
        var original = new PersonV2 { Name = null, Age = 1, Home = null };

        PersonV2 copy = _copiers.GetRequiredCopier<PersonV2>().DeepCopy(original, new CopyContext());

        Assert.NotSame(original, copy);
        Assert.Null(copy.Name);
        Assert.Null(copy.Home);
        Assert.Equal(1, copy.Age);
    }

    // =====================================================================
    // Types under test
    // =====================================================================

    // Tag (Id 20) is deliberately the HIGHEST id and known to BOTH versions: when v1 reads a v2
    // payload it must correctly skip every unknown field in between to land on Tag. A desynced skip
    // of any wire type would clobber or fail before Tag is reached, so asserting Tag survived gives
    // the forward-compat tests real teeth for every SkipField branch.
    private sealed class PersonV1
    {
        public string? Name { get; set; }   // [Id 1]
        public int Age { get; set; }         // [Id 2]
        public string? Tag { get; set; }     // [Id 20]
    }

    // Superset of PersonV1 sharing ids 1, 2 & 20, plus non-contiguous extra ids covering every
    // SkipField wire-type branch.
    private sealed class PersonV2
    {
        public string? Name { get; set; }      // [Id 1]  LengthPrefixed / Extended-null
        public int Age { get; set; }            // [Id 2]  VarInt
        public string? Email { get; set; }      // [Id 5]  LengthPrefixed
        public double Score { get; set; }       // [Id 6]  Fixed64
        public float Rating { get; set; }       // [Id 7]  Fixed32
        public Address? Home { get; set; }      // [Id 8]  TagDelimited (nested) / Extended-null
        public string? Nickname { get; set; }   // [Id 9]  Extended-null when null
        public int Version { get; set; }        // [Id 10] VarInt
        public string? Tag { get; set; }        // [Id 20] LengthPrefixed (known to v1)
    }

    private sealed class Address
    {
        public string? Street { get; set; }   // [Id 1]
        public int Zip { get; set; }           // [Id 2]
    }

    // =====================================================================
    // Hand-written codecs — mirror SerializerGenerator output exactly
    // =====================================================================

    private static void SkipField(CodecReader reader, Field field)
    {
        switch (field.WireType)
        {
            case WireType.VarInt:
                reader.ReadVarUInt64();
                break;
            case WireType.Fixed32:
                reader.ReadFixed32();
                break;
            case WireType.Fixed64:
                reader.ReadFixed64();
                break;
            case WireType.LengthPrefixed:
                reader.ReadBytes();
                break;
            case WireType.TagDelimited:
                Field nested;
                while (!(nested = reader.ReadFieldHeader()).IsEndObject)
                {
                    SkipField(reader, nested);
                }

                break;
            case WireType.Extended:
            case WireType.EndTagDelimited:
                break;
        }
    }

    private sealed class AddressCodec(ICodecProvider codecs) : IFieldCodec<Address>
    {
        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, Address value)
        {
            if (value is null)
            {
                writer.WriteFieldHeader(fieldId, WireType.Extended);
                writer.WriteByte((byte)ExtendedWireType.Null);
                return;
            }

            writer.WriteFieldHeader(fieldId, WireType.TagDelimited);
            codecs.GetRequiredCodec<string?>().WriteField(writer, 1u, typeof(string), value.Street);
            codecs.GetRequiredCodec<int>().WriteField(writer, 2u, typeof(int), value.Zip);
            writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
        }

        public Address ReadValue(CodecReader reader, Field field)
        {
            if (field.WireType == WireType.Extended)
            {
                return default!;
            }

            var result = new Address();
            Field f;
            while (!(f = reader.ReadFieldHeader()).IsEndObject)
            {
                switch ((int)f.FieldId)
                {
                    case 1: result.Street = codecs.GetRequiredCodec<string?>().ReadValue(reader, f); break;
                    case 2: result.Zip = codecs.GetRequiredCodec<int>().ReadValue(reader, f); break;
                    default: SkipField(reader, f); break;
                }
            }

            return result;
        }
    }

    private sealed class PersonV1Codec(ICodecProvider codecs) : IFieldCodec<PersonV1>
    {
        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, PersonV1 value)
        {
            if (value is null)
            {
                writer.WriteFieldHeader(fieldId, WireType.Extended);
                writer.WriteByte((byte)ExtendedWireType.Null);
                return;
            }

            writer.WriteFieldHeader(fieldId, WireType.TagDelimited);
            codecs.GetRequiredCodec<string?>().WriteField(writer, 1u, typeof(string), value.Name);
            codecs.GetRequiredCodec<int>().WriteField(writer, 2u, typeof(int), value.Age);
            codecs.GetRequiredCodec<string?>().WriteField(writer, 20u, typeof(string), value.Tag);
            writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
        }

        public PersonV1 ReadValue(CodecReader reader, Field field)
        {
            if (field.WireType == WireType.Extended)
            {
                return default!;
            }

            var result = new PersonV1();
            Field f;
            while (!(f = reader.ReadFieldHeader()).IsEndObject)
            {
                switch ((int)f.FieldId)
                {
                    case 1: result.Name = codecs.GetRequiredCodec<string?>().ReadValue(reader, f); break;
                    case 2: result.Age = codecs.GetRequiredCodec<int>().ReadValue(reader, f); break;
                    case 20: result.Tag = codecs.GetRequiredCodec<string?>().ReadValue(reader, f); break;
                    default: SkipField(reader, f); break;
                }
            }

            return result;
        }
    }

    private sealed class PersonV2Codec(ICodecProvider codecs) : IFieldCodec<PersonV2>
    {
        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, PersonV2 value)
        {
            if (value is null)
            {
                writer.WriteFieldHeader(fieldId, WireType.Extended);
                writer.WriteByte((byte)ExtendedWireType.Null);
                return;
            }

            writer.WriteFieldHeader(fieldId, WireType.TagDelimited);
            codecs.GetRequiredCodec<string?>().WriteField(writer, 1u, typeof(string), value.Name);
            codecs.GetRequiredCodec<int>().WriteField(writer, 2u, typeof(int), value.Age);
            codecs.GetRequiredCodec<string?>().WriteField(writer, 5u, typeof(string), value.Email);
            codecs.GetRequiredCodec<double>().WriteField(writer, 6u, typeof(double), value.Score);
            codecs.GetRequiredCodec<float>().WriteField(writer, 7u, typeof(float), value.Rating);
            codecs.GetRequiredCodec<Address>().WriteField(writer, 8u, typeof(Address), value.Home!);
            codecs.GetRequiredCodec<string?>().WriteField(writer, 9u, typeof(string), value.Nickname);
            codecs.GetRequiredCodec<int>().WriteField(writer, 10u, typeof(int), value.Version);
            codecs.GetRequiredCodec<string?>().WriteField(writer, 20u, typeof(string), value.Tag);
            writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
        }

        public PersonV2 ReadValue(CodecReader reader, Field field)
        {
            if (field.WireType == WireType.Extended)
            {
                return default!;
            }

            var result = new PersonV2();
            Field f;
            while (!(f = reader.ReadFieldHeader()).IsEndObject)
            {
                switch ((int)f.FieldId)
                {
                    case 1: result.Name = codecs.GetRequiredCodec<string?>().ReadValue(reader, f); break;
                    case 2: result.Age = codecs.GetRequiredCodec<int>().ReadValue(reader, f); break;
                    case 5: result.Email = codecs.GetRequiredCodec<string?>().ReadValue(reader, f); break;
                    case 6: result.Score = codecs.GetRequiredCodec<double>().ReadValue(reader, f); break;
                    case 7: result.Rating = codecs.GetRequiredCodec<float>().ReadValue(reader, f); break;
                    case 8: result.Home = codecs.GetRequiredCodec<Address>().ReadValue(reader, f); break;
                    case 9: result.Nickname = codecs.GetRequiredCodec<string?>().ReadValue(reader, f); break;
                    case 10: result.Version = codecs.GetRequiredCodec<int>().ReadValue(reader, f); break;
                    case 20: result.Tag = codecs.GetRequiredCodec<string?>().ReadValue(reader, f); break;
                    default: SkipField(reader, f); break;
                }
            }

            return result;
        }
    }

    // =====================================================================
    // Hand-written copiers — mirror SerializerGenerator DeepCopy output exactly
    // =====================================================================

    private sealed class AddressCopier(ICopierProvider copiers) : IDeepCopier<Address>
    {
        public Address DeepCopy(Address input, CopyContext context)
        {
            if (input is null) return default!;
            Address? existing = context.TryGetCopy<Address>(input);
            if (existing is not null) return existing;

            var copy = new Address();
            context.RecordCopy(input, copy);
            copy.Street = copiers.GetRequiredCopier<string?>().DeepCopy(input.Street, context);
            copy.Zip = copiers.GetRequiredCopier<int>().DeepCopy(input.Zip, context);
            return copy;
        }
    }

    private sealed class PersonV1Copier(ICopierProvider copiers) : IDeepCopier<PersonV1>
    {
        public PersonV1 DeepCopy(PersonV1 input, CopyContext context)
        {
            if (input is null) return default!;
            PersonV1? existing = context.TryGetCopy<PersonV1>(input);
            if (existing is not null) return existing;

            var copy = new PersonV1();
            context.RecordCopy(input, copy);
            copy.Name = copiers.GetRequiredCopier<string?>().DeepCopy(input.Name, context);
            copy.Age = copiers.GetRequiredCopier<int>().DeepCopy(input.Age, context);
            copy.Tag = copiers.GetRequiredCopier<string?>().DeepCopy(input.Tag, context);
            return copy;
        }
    }

    private sealed class PersonV2Copier(ICopierProvider copiers) : IDeepCopier<PersonV2>
    {
        public PersonV2 DeepCopy(PersonV2 input, CopyContext context)
        {
            if (input is null) return default!;
            PersonV2? existing = context.TryGetCopy<PersonV2>(input);
            if (existing is not null) return existing;

            var copy = new PersonV2();
            context.RecordCopy(input, copy);
            copy.Name = copiers.GetRequiredCopier<string?>().DeepCopy(input.Name, context);
            copy.Age = copiers.GetRequiredCopier<int>().DeepCopy(input.Age, context);
            copy.Email = copiers.GetRequiredCopier<string?>().DeepCopy(input.Email, context);
            copy.Score = copiers.GetRequiredCopier<double>().DeepCopy(input.Score, context);
            copy.Rating = copiers.GetRequiredCopier<float>().DeepCopy(input.Rating, context);
            copy.Home = copiers.GetRequiredCopier<Address>().DeepCopy(input.Home!, context);
            copy.Nickname = copiers.GetRequiredCopier<string?>().DeepCopy(input.Nickname, context);
            copy.Version = copiers.GetRequiredCopier<int>().DeepCopy(input.Version, context);
            copy.Tag = copiers.GetRequiredCopier<string?>().DeepCopy(input.Tag, context);
            return copy;
        }
    }
}
