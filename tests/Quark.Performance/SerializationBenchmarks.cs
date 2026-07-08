using System.Buffers;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization;
using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Attributes;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance;

/// <summary>
/// Benchmarks for serialization performance using Quark's serialization system.
/// Types are annotated with [GenerateSerializer] (for the code generator) and also
/// have hand-written codecs registered (for benchmark projects without the generator).
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private IServiceProvider? _services;
    private ISerializer? _serializer;
    private SimpleMessage? _simpleMessage;
    private ComplexMessage? _complexMessage;
    private byte[]? _serializedSimple;
    private byte[]? _serializedComplex;
    private readonly ArrayBufferWriter<byte> _buffer = new(1024);

    [GlobalSetup]
    public void GlobalSetup()
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();

        // Register our custom codecs in the same container as primitives
        services.AddSingleton<IFieldCodec<SimpleMessage>>(sp => new SimpleMessageCodec(sp.GetRequiredService<ICodecProvider>()));
        services.AddSingleton<IDeepCopier<SimpleMessage>>(new SimpleMessageCopier());
        services.AddSingleton<IFieldCodec<ComplexMessage>>(sp => new ComplexMessageCodec(sp.GetRequiredService<ICodecProvider>()));
        services.AddSingleton<IDeepCopier<ComplexMessage>>(new ComplexMessageCopier());
        services.AddSingleton<IFieldCodec<NestedMessage>>(sp => new NestedMessageCodec(sp.GetRequiredService<ICodecProvider>()));
        services.AddSingleton<IDeepCopier<NestedMessage>>(new NestedMessageCopier());

        _services = services.BuildServiceProvider();
        _serializer = _services.GetRequiredService<ISerializer>();

        _simpleMessage = new SimpleMessage
        {
            Id = 42,
            Content = "Hello, World!"
        };

        _complexMessage = new ComplexMessage
        {
            Id = 12345,
            Timestamp = DateTime.UtcNow,
            Score = 99.99,
            Rating = 4.5f,
            Version = 7,
            Nested = new NestedMessage
            {
                Name = "Nested",
                Value = 99.99
            },
            Tag = "benchmark"
        };

        // Pre-serialize for deserialization benchmarks
        _serializedSimple = SerializeToBytes(_simpleMessage);
        _serializedComplex = SerializeToBytes(_complexMessage);
    }

    private byte[] SerializeToBytes<T>(T obj)
    {
        _buffer.Clear();
        _serializer!.Serialize(_buffer, obj);
        return _buffer.WrittenSpan.ToArray();
    }

    private T DeserializeFromBytes<T>(byte[] data)
    {
        return _serializer!.Deserialize<T>(data.AsMemory())!;
    }

    [Benchmark]
    public byte[] SerializeSimple()
    {
        return SerializeToBytes(_simpleMessage!);
    }

    [Benchmark]
    public SimpleMessage DeserializeSimple()
    {
        return DeserializeFromBytes<SimpleMessage>(_serializedSimple!);
    }

    [Benchmark]
    public byte[] SerializeComplex()
    {
        return SerializeToBytes(_complexMessage!);
    }

    [Benchmark]
    public ComplexMessage DeserializeComplex()
    {
        return DeserializeFromBytes<ComplexMessage>(_serializedComplex!);
    }

    [Benchmark]
    public SimpleMessage SerializeRoundTripSimple()
    {
        var bytes = SerializeToBytes(_simpleMessage!);
        return DeserializeFromBytes<SimpleMessage>(bytes);
    }

    [Benchmark]
    public ComplexMessage SerializeRoundTripComplex()
    {
        var bytes = SerializeToBytes(_complexMessage!);
        return DeserializeFromBytes<ComplexMessage>(bytes);
    }
}

// =========================================================================
// Benchmark data types
// =========================================================================

[GenerateSerializer]
public class SimpleMessage
{
    [Id(0)] public int Id { get; set; }
    [Id(1)] public string Content { get; set; } = "";
}

[GenerateSerializer]
public class ComplexMessage
{
    [Id(0)] public int Id { get; set; }
    [Id(1)] public DateTime Timestamp { get; set; }
    [Id(2)] public double Score { get; set; }
    [Id(3)] public float Rating { get; set; }
    [Id(4)] public long Version { get; set; }
    [Id(5)] public NestedMessage? Nested { get; set; }
    [Id(6)] public string? Tag { get; set; }
}

[GenerateSerializer]
public class NestedMessage
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public double Value { get; set; }
}

// =========================================================================
// Hand-written codecs — mirror SerializerGenerator output
// =========================================================================

internal static class SerializationHelpers
{
    public static void SkipField(CodecReader reader, Field field)
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
}

public sealed class SimpleMessageCodec(ICodecProvider codecs) : IFieldCodec<SimpleMessage>
{
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, SimpleMessage? value)
    {
        if (value is null)
        {
            writer.WriteFieldHeader(fieldId, WireType.Extended);
            writer.WriteByte((byte)ExtendedWireType.Null);
            return;
        }

        writer.WriteFieldHeader(fieldId, WireType.TagDelimited);
        codecs.GetRequiredCodec<int>().WriteField(writer, 0u, typeof(int), value.Id);
        codecs.GetRequiredCodec<string?>().WriteField(writer, 1u, typeof(string), value.Content);
        writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
    }

    public SimpleMessage ReadValue(CodecReader reader, Field field)
    {
        if (field.WireType == WireType.Extended)
        {
            return default!;
        }

        var result = new SimpleMessage();
        Field f;
        while (!(f = reader.ReadFieldHeader()).IsEndObject)
        {
            switch ((int)f.FieldId)
            {
                case 0: result.Id = codecs.GetRequiredCodec<int>().ReadValue(reader, f); break;
                case 1: result.Content = codecs.GetRequiredCodec<string?>().ReadValue(reader, f) ?? ""; break;
                default: SerializationHelpers.SkipField(reader, f); break;
            }
        }

        return result;
    }
}

public sealed class SimpleMessageCopier : IDeepCopier<SimpleMessage>
{
    public SimpleMessage DeepCopy(SimpleMessage input, CopyContext context)
    {
        if (context.TryGetCopy<SimpleMessage>(input) is { } existing) return existing;
        var copy = new SimpleMessage { Id = input.Id, Content = input.Content };
        context.RecordCopy(input, copy);
        return copy;
    }
}

public sealed class NestedMessageCodec(ICodecProvider codecs) : IFieldCodec<NestedMessage>
{
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, NestedMessage? value)
    {
        if (value is null)
        {
            writer.WriteFieldHeader(fieldId, WireType.Extended);
            writer.WriteByte((byte)ExtendedWireType.Null);
            return;
        }

        writer.WriteFieldHeader(fieldId, WireType.TagDelimited);
        codecs.GetRequiredCodec<string?>().WriteField(writer, 0u, typeof(string), value.Name);
        codecs.GetRequiredCodec<double>().WriteField(writer, 1u, typeof(double), value.Value);
        writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
    }

    public NestedMessage ReadValue(CodecReader reader, Field field)
    {
        if (field.WireType == WireType.Extended)
        {
            return default!;
        }

        var result = new NestedMessage();
        Field f;
        while (!(f = reader.ReadFieldHeader()).IsEndObject)
        {
            switch ((int)f.FieldId)
            {
                case 0: result.Name = codecs.GetRequiredCodec<string?>().ReadValue(reader, f) ?? ""; break;
                case 1: result.Value = codecs.GetRequiredCodec<double>().ReadValue(reader, f); break;
                default: SerializationHelpers.SkipField(reader, f); break;
            }
        }

        return result;
    }
}

public sealed class NestedMessageCopier : IDeepCopier<NestedMessage>
{
    public NestedMessage DeepCopy(NestedMessage input, CopyContext context)
    {
        if (context.TryGetCopy<NestedMessage>(input) is { } existing) return existing;
        var copy = new NestedMessage { Name = input.Name, Value = input.Value };
        context.RecordCopy(input, copy);
        return copy;
    }
}

public sealed class ComplexMessageCodec(ICodecProvider codecs) : IFieldCodec<ComplexMessage>
{
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, ComplexMessage? value)
    {
        if (value is null)
        {
            writer.WriteFieldHeader(fieldId, WireType.Extended);
            writer.WriteByte((byte)ExtendedWireType.Null);
            return;
        }

        writer.WriteFieldHeader(fieldId, WireType.TagDelimited);
        codecs.GetRequiredCodec<int>().WriteField(writer, 0u, typeof(int), value.Id);
        codecs.GetRequiredCodec<DateTime>().WriteField(writer, 1u, typeof(DateTime), value.Timestamp);
        codecs.GetRequiredCodec<double>().WriteField(writer, 2u, typeof(double), value.Score);
        codecs.GetRequiredCodec<float>().WriteField(writer, 3u, typeof(float), value.Rating);
        codecs.GetRequiredCodec<long>().WriteField(writer, 4u, typeof(long), value.Version);
        codecs.GetRequiredCodec<NestedMessage>().WriteField(writer, 5u, typeof(NestedMessage), value.Nested!);
        codecs.GetRequiredCodec<string?>().WriteField(writer, 6u, typeof(string), value.Tag);
        writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
    }

    public ComplexMessage ReadValue(CodecReader reader, Field field)
    {
        if (field.WireType == WireType.Extended)
        {
            return default!;
        }

        var result = new ComplexMessage();
        Field f;
        while (!(f = reader.ReadFieldHeader()).IsEndObject)
        {
            switch ((int)f.FieldId)
            {
                case 0: result.Id = codecs.GetRequiredCodec<int>().ReadValue(reader, f); break;
                case 1: result.Timestamp = codecs.GetRequiredCodec<DateTime>().ReadValue(reader, f); break;
                case 2: result.Score = codecs.GetRequiredCodec<double>().ReadValue(reader, f); break;
                case 3: result.Rating = codecs.GetRequiredCodec<float>().ReadValue(reader, f); break;
                case 4: result.Version = codecs.GetRequiredCodec<long>().ReadValue(reader, f); break;
                case 5: result.Nested = codecs.GetRequiredCodec<NestedMessage>().ReadValue(reader, f); break;
                case 6: result.Tag = codecs.GetRequiredCodec<string?>().ReadValue(reader, f); break;
                default: SerializationHelpers.SkipField(reader, f); break;
            }
        }

        return result;
    }
}

public sealed class ComplexMessageCopier : IDeepCopier<ComplexMessage>
{
    public ComplexMessage DeepCopy(ComplexMessage input, CopyContext context)
    {
        if (context.TryGetCopy<ComplexMessage>(input) is { } existing) return existing;
        var copy = new ComplexMessage
        {
            Id = input.Id,
            Timestamp = input.Timestamp,
            Score = input.Score,
            Rating = input.Rating,
            Version = input.Version,
            Nested = input.Nested is not null ? new NestedMessage { Name = input.Nested.Name, Value = input.Nested.Value } : null,
            Tag = input.Tag
        };
        context.RecordCopy(input, copy);
        return copy;
    }
}
