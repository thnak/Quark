using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.CodeGenerator;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class SerializerGeneratorTests
{
    [Fact]
    public void Generates_Codec_And_Copier_For_Attributed_Type()
    {
        const string source = """
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Person
                              {
                                  [Id(0)] public string? Name { get; set; }
                                  [Id(1)] public int Age { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class PersonCodec", generated);
        Assert.Contains("internal sealed class PersonCopier", generated);
        Assert.Contains("if (value is null)", generated);
        Assert.Contains("if (field.WireType == global::Quark.Serialization.Abstractions.Buffers.WireType.Extended", generated);
        Assert.Contains("case 0: result.Name =", generated);
        Assert.Contains("case 1: result.Age =", generated);
        Assert.Contains("private readonly global::Quark.Serialization.Abstractions.Abstractions.ICopierProvider _copiers;",
            generated);
        Assert.Contains("_copiers.GetRequiredCopier<", generated);
        Assert.Contains("DeepCopy(input.Name, context)", generated);
        // CloneStatic() — DI-free shallow clone for use by generated invokable Clone() methods.
        Assert.Contains("public static", generated);
        Assert.Contains("CloneStatic(", generated);
        Assert.Contains("Name = input.Name,", generated);
        Assert.Contains("Age = input.Age,", generated);
        // WriteStatic / ReadStatic — DI-free positional binary codec for transport path.
        Assert.Contains("WriteStatic(", generated);
        Assert.Contains("ReadStatic(", generated);
        Assert.Contains("writer.WriteString(value.Name);", generated);
        Assert.Contains("writer.WriteInt32(value.Age);", generated);
        Assert.Contains("Name = reader.ReadString(),", generated);
        Assert.Contains("Age = reader.ReadInt32(),", generated);
    }

    [Fact]
    public void Ignores_Types_Without_Id_Members()
    {
        const string source = """
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class EmptyPayload
                              {
                                  public string? Name { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generates_Correct_ReadWrite_For_Enum_Member_In_Dto()
    {
        const string source = """
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              public enum Priority { Low = 0, Normal = 1, High = 2 }

                              [GenerateSerializer]
                              public sealed class TaskDto
                              {
                                  [Id(0)] public string? Name { get; set; }
                                  [Id(1)] public Priority Level { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // WriteStatic: enum field written as cast-to-int
        Assert.Contains("writer.WriteInt32((int)value.Level);", generated);
        // ReadStatic: read int and cast back to enum type
        Assert.Contains("Level = (global::Demo.Priority)reader.ReadInt32(),", generated);
        // Must not fall back to boxed WriteValue/ReadArg for the enum member
        Assert.DoesNotContain("WriteValue(writer, value.Level)", generated);
    }

    // -----------------------------------------------------------------------
    // Immutable collection tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ImmutableList_Member_Generates_Field_Codec_And_Static_Paths()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Bag
                              {
                                  [Id(0)] public ImmutableList<int>? Items { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Field-codec path: per-element WriteField calls with field id 1u (int uses keyword form)
        Assert.Contains("WireType.TagDelimited", generated);
        Assert.Contains("_codecs.GetRequiredCodec<int>().WriteField(writer, 1u,", generated);
        Assert.Contains("ImmutableList.CreateBuilder<int>()", generated);
        Assert.Contains("_ef.FieldId == 1u", generated);
        Assert.Contains(".ToImmutable()", generated);

        // Static path: presence byte + count + element write loop
        Assert.Contains("ReadCollection_Items(ref reader)", generated);
        Assert.Contains("writer.WriteVarUInt32((uint)value.Items.Count);", generated);
        Assert.Contains("writer.WriteInt32(_item);", generated);

        // ReadCollection_Items helper
        Assert.Contains("private static", generated);
        Assert.Contains("ReadCollection_Items(", generated);
        Assert.Contains("reader.ReadVarUInt32()", generated);
        Assert.Contains("reader.ReadInt32()", generated);
    }

    [Fact]
    public void ImmutableHashSet_Member_Generates_Correct_Code()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class TagSet
                              {
                                  [Id(0)] public ImmutableHashSet<string>? Tags { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // string uses keyword form in FullyQualifiedFormat
        Assert.Contains("ImmutableHashSet.CreateBuilder<string>()", generated);
        Assert.Contains("_codecs.GetRequiredCodec<string>().WriteField(writer, 1u,", generated);
        Assert.Contains("ReadCollection_Tags(ref reader)", generated);
        Assert.Contains("writer.WriteString(_item);", generated);
        Assert.Contains("reader.ReadString()", generated);
    }

    [Fact]
    public void ImmutableSortedSet_Member_Generates_Correct_Code()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class GuidSet
                              {
                                  [Id(0)] public ImmutableSortedSet<System.Guid>? Keys { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Guid is not a keyword type — uses global:: form
        Assert.Contains("ImmutableSortedSet.CreateBuilder<global::System.Guid>()", generated);
        Assert.Contains("ReadCollection_Keys(ref reader)", generated);
        Assert.Contains("writer.WriteRaw(_item.ToByteArray());", generated);
        Assert.Contains("new global::System.Guid(reader.ReadRaw(16))", generated);
    }

    [Fact]
    public void ImmutableArray_Member_Generates_IsDefault_Null_Handling()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Vec
                              {
                                  [Id(0)] public ImmutableArray<float> Values { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Field-codec path: .IsDefault check → Extended/Null wire type
        Assert.Contains("value.Values.IsDefault", generated);
        Assert.Contains("WireType.Extended", generated);
        Assert.Contains("ExtendedWireType.Null", generated);

        // Static path: .IsDefault presence check, .Length for count, builder with capacity
        // float uses keyword form
        Assert.Contains("value.Values.Length", generated);
        Assert.Contains("ImmutableArray.CreateBuilder<float>((int)_count)", generated);
        Assert.Contains("_builder.MoveToImmutable()", generated);

        // ReadCollection default return for value-type ImmutableArray
        Assert.Contains("if (reader.ReadByte() == 0) return default;", generated);
    }

    [Fact]
    public void ImmutableDictionary_Member_Generates_Key_Value_Field_Ids()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Counter
                              {
                                  [Id(0)] public ImmutableDictionary<string, int>? Counts { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Field-codec path: key at field id 1u, value at field id 2u (keyword forms for string/int)
        Assert.Contains("_codecs.GetRequiredCodec<string>().WriteField(writer, 1u,", generated);
        Assert.Contains("_codecs.GetRequiredCodec<int>().WriteField(writer, 2u,", generated);
        Assert.Contains("ImmutableDictionary.CreateBuilder<string, int>()", generated);

        // Static path helper
        Assert.Contains("ReadCollection_Counts(ref reader)", generated);
        Assert.Contains("writer.WriteString(_kvp.Key);", generated);
        Assert.Contains("writer.WriteInt32(_kvp.Value);", generated);
        Assert.Contains("reader.ReadString()", generated);
        Assert.Contains("reader.ReadInt32()", generated);
        Assert.Contains("_builder.Add(_k, _v);", generated);
    }

    [Fact]
    public void ImmutableSortedDictionary_Member_Generates_Correct_Builder()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Scores
                              {
                                  [Id(0)] public ImmutableSortedDictionary<int, string>? Entries { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // keyword forms for int and string
        Assert.Contains("ImmutableSortedDictionary.CreateBuilder<int, string>()", generated);
        Assert.Contains("ReadCollection_Entries(ref reader)", generated);
        Assert.Contains("writer.WriteInt32(_kvp.Key);", generated);
        Assert.Contains("writer.WriteString(_kvp.Value);", generated);
    }

    [Fact]
    public void QRK0054_Emitted_For_Unsupported_Element_Type()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using System.Collections.Generic;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Bad
                              {
                                  [Id(0)] public ImmutableList<List<int>>? Items { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        Diagnostic[] errors = result.Diagnostics.Where(d => d.Id == "QRK0054").ToArray();
        Assert.True(errors.Length > 0, "Expected QRK0054 diagnostic");
        Assert.Equal(DiagnosticSeverity.Error, errors[0].Severity);
    }

    [Fact]
    public void QRK0054_Emitted_For_Nested_Immutable_Collection_Element()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Nested
                              {
                                  [Id(0)] public ImmutableList<ImmutableList<int>>? Groups { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        Diagnostic[] errors = result.Diagnostics.Where(d => d.Id == "QRK0054").ToArray();
        Assert.True(errors.Length > 0, "Expected QRK0054 diagnostic for a collection-of-collection member");
        Assert.Equal(DiagnosticSeverity.Error, errors[0].Severity);
    }

    [Fact]
    public void QRK0054_Emitted_For_Nested_Immutable_Collection_Dictionary_Value()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class NestedMap
                              {
                                  [Id(0)] public ImmutableDictionary<string, ImmutableList<int>>? Groups { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        Diagnostic[] errors = result.Diagnostics.Where(d => d.Id == "QRK0054").ToArray();
        Assert.True(errors.Length > 0, "Expected QRK0054 diagnostic for a collection-valued dictionary member");
        Assert.Equal(DiagnosticSeverity.Error, errors[0].Severity);
    }

    [Fact]
    public void Nested_GenerateSerializer_Element_In_ImmutableList()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Address
                              {
                                  [Id(0)] public string? Street { get; set; }
                              }

                              [GenerateSerializer]
                              public sealed class Person
                              {
                                  [Id(0)] public ImmutableList<Address>? Addresses { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);

        // Two types → two generated sources; select the Person one
        string personGenerated = result.GeneratedSources
            .Single(s => s.Contains("class PersonCopier"));

        // Field-codec path uses the codec for Address (non-keyword type → global:: form)
        Assert.Contains("_codecs.GetRequiredCodec<global::Demo.Address>()", personGenerated);

        // Static path: WriteStatic and ReadStatic of AddressCopier appear in loop and helper
        Assert.Contains("global::Demo.AddressCopier.WriteStatic(ref writer, _item);", personGenerated);
        Assert.Contains("global::Demo.AddressCopier.ReadStatic(ref reader)", personGenerated);
    }

    [Fact]
    public void DeepCopy_Uses_Identity_For_Collection_Members_And_Copier_For_Scalars()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Mixed
                              {
                                  [Id(0)] public ImmutableList<int>? Numbers { get; set; }
                                  [Id(1)] public string? Label { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Collection member: identity copy (immutable collections are safe to share by reference)
        Assert.Contains("copy.Numbers = input.Numbers;", generated);
        // Scalar member: copier-based deep copy (string uses keyword form)
        Assert.Contains("_copiers.GetRequiredCopier<string>().DeepCopy(input.Label, context)", generated);
    }

    // -----------------------------------------------------------------------
    // List<T> / Dictionary<K,V> / T[] / ImmutableStack<T> / ImmutableQueue<T>
    // -----------------------------------------------------------------------

    [Fact]
    public void List_Member_Generates_Field_Codec_And_Static_Paths()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Bag
                              {
                                  [Id(0)] public List<int>? Items { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Field-codec path: shares the same per-element TagDelimited loop as ImmutableList,
        // but reads back into a plain List<T> (no ToImmutable()).
        Assert.Contains("WireType.TagDelimited", generated);
        Assert.Contains("_codecs.GetRequiredCodec<int>().WriteField(writer, 1u,", generated);
        Assert.Contains("new global::System.Collections.Generic.List<int>()", generated);
        Assert.Contains("result.Items = _b_Items;", generated);

        // Static path
        Assert.Contains("writer.WriteVarUInt32((uint)value.Items.Count);", generated);
        Assert.Contains("writer.WriteInt32(_item);", generated);
        Assert.Contains("ReadCollection_Items(ref reader)", generated);
        Assert.Contains("new global::System.Collections.Generic.List<int>((int)_count);", generated);
        Assert.DoesNotContain("ToImmutable()", generated);
    }

    [Fact]
    public void Dictionary_Member_Generates_Key_Value_Field_Ids()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Counter
                              {
                                  [Id(0)] public Dictionary<string, int>? Counts { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        Assert.Contains("_codecs.GetRequiredCodec<string>().WriteField(writer, 1u,", generated);
        Assert.Contains("_codecs.GetRequiredCodec<int>().WriteField(writer, 2u,", generated);
        Assert.Contains("new global::System.Collections.Generic.Dictionary<string, int>()", generated);
        Assert.Contains("result.Counts = _b_Counts;", generated);

        Assert.Contains("ReadCollection_Counts(ref reader)", generated);
        Assert.Contains("new global::System.Collections.Generic.Dictionary<string, int>((int)_count);", generated);
        Assert.Contains("_result.Add(_k, _v);", generated);
    }

    [Fact]
    public void Array_Member_Generates_Length_Based_Static_Path_And_ToArray_Field_Codec()
    {
        const string source = """
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Vec
                              {
                                  [Id(0)] public int[]? Values { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Field-codec path: buffered into a List<T>, then .ToArray()
        Assert.Contains("result.Values = _b_Values.ToArray();", generated);

        // Static path: .Length (not .Count) for the presence/size check
        Assert.Contains("writer.WriteVarUInt32((uint)value.Values.Length);", generated);
        Assert.Contains("ReadCollection_Values(ref reader)", generated);
        Assert.Contains("var _result = new int[_count];", generated);
        Assert.Contains("_result[_i] = reader.ReadInt32();", generated);
    }

    [Fact]
    public void ByteArray_Member_Still_Uses_Dedicated_Fallback_Codec_Not_Generic_Array_Path()
    {
        // byte[] must keep using the existing ByteArrayCodec / GrainMessageSerializer.ByteArray
        // fast path rather than being swept into the new generic per-element array handling —
        // that would silently change the wire format and regress performance.
        const string source = """
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Blob
                              {
                                  [Id(0)] public byte[]? Data { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        Assert.Contains("_codecs.GetRequiredCodec<byte[]>().WriteField(writer, 0u, typeof(byte[]), value.Data);", generated);
        Assert.DoesNotContain("ReadCollection_Data", generated);
    }

    [Fact]
    public void ImmutableQueue_Member_Enqueues_In_Wire_Order()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Pending
                              {
                                  [Id(0)] public ImmutableQueue<int>? Items { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Field-codec read: Enqueue directly during the read loop (FIFO enumeration order
        // round-trips with plain sequential Enqueue — no buffering needed).
        Assert.Contains("global::System.Collections.Immutable.ImmutableQueue<int>.Empty;", generated);
        Assert.Contains("_q_Items = _q_Items.Enqueue(", generated);
        Assert.Contains("result.Items = _q_Items;", generated);

        // Static path: two-pass count (no O(1) Count on ImmutableQueue<T>), then Enqueue loop
        Assert.Contains("int _count = 0;", generated);
        Assert.Contains("foreach (var _item in value.Items) _count++;", generated);
        Assert.Contains("_result = _result.Enqueue(reader.ReadInt32());", generated);
    }

    [Fact]
    public void ImmutableStack_Member_Reconstructs_Original_Push_Order()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class History
                              {
                                  [Id(0)] public ImmutableStack<int>? Items { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Field-codec read: buffer into a List in wire (top-to-bottom) order, then push
        // back-to-front so the reconstructed stack has the same top as the original —
        // a naive sequential-Push reconstruction would reverse the stack.
        Assert.Contains("var _t_Items = new global::System.Collections.Generic.List<int>();", generated);
        Assert.Contains("for (int _i = _t_Items.Count - 1; _i >= 0; _i--) _s_Items = _s_Items.Push(_t_Items[_i]);", generated);

        // Static path read helper: same buffer-then-reverse-push pattern
        Assert.Contains("ReadCollection_Items(ref reader)", generated);
        Assert.Contains("var _tmp = new int[_count];", generated);
        Assert.Contains("for (int _i = (int)_count - 1; _i >= 0; _i--) _result = _result.Push(_tmp[_i]);", generated);
    }

    [Fact]
    public void QRK0054_Emitted_For_Nested_Collection_In_Mutable_List()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Bad
                              {
                                  [Id(0)] public List<List<int>>? Groups { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        Diagnostic[] errors = result.Diagnostics.Where(d => d.Id == "QRK0054").ToArray();
        Assert.True(errors.Length > 0, "Expected QRK0054 diagnostic for a List<List<T>> member");
        Assert.Equal(DiagnosticSeverity.Error, errors[0].Severity);
    }

    [Fact]
    public void QRK0054_Emitted_For_Array_Valued_Dictionary()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Bad
                              {
                                  [Id(0)] public Dictionary<string, int[]>? Map { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        Diagnostic[] errors = result.Diagnostics.Where(d => d.Id == "QRK0054").ToArray();
        Assert.True(errors.Length > 0, "Expected QRK0054 diagnostic for a Dictionary<K, T[]> member");
        Assert.Equal(DiagnosticSeverity.Error, errors[0].Severity);
    }

    [Fact]
    public void DeepCopy_And_CloneStatic_Use_New_Container_For_Mutable_Collections()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Mixed
                              {
                                  [Id(0)] public List<int>? Numbers { get; set; }
                                  [Id(1)] public Dictionary<string, int>? Counts { get; set; }
                                  [Id(2)] public int[]? Values { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // DeepCopy: new container, not identity/reference sharing (these are mutable)
        Assert.Contains(
            "copy.Numbers = input.Numbers is null ? default! : new global::System.Collections.Generic.List<int>(input.Numbers);",
            generated);
        Assert.Contains(
            "copy.Counts = input.Counts is null ? default! : new global::System.Collections.Generic.Dictionary<string, int>(input.Counts);",
            generated);
        Assert.Contains("copy.Values = input.Values is null ? default! : (int[])input.Values.Clone();", generated);

        // CloneStatic: same new-container convention (matches GrainProxyGenerator's CloneKind)
        Assert.Contains(
            "Numbers = input.Numbers is null ? default! : new global::System.Collections.Generic.List<int>(input.Numbers),",
            generated);
        Assert.Contains("Values = input.Values is null ? default! : (int[])input.Values.Clone(),", generated);
    }

    [Fact]
    public void DeepCopy_Uses_Identity_For_ImmutableStack_And_ImmutableQueue()
    {
        const string source = """
                              using System.Collections.Immutable;
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Mixed
                              {
                                  [Id(0)] public ImmutableStack<int>? Undo { get; set; }
                                  [Id(1)] public ImmutableQueue<int>? Pending { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        Assert.Contains("copy.Undo = input.Undo;", generated);
        Assert.Contains("copy.Pending = input.Pending;", generated);
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}
