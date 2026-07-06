using System.Buffers;
using Quark.Core.Abstractions.Reminders;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Serializes request/response payloads for transport-routed grain invocation.
/// </summary>
public sealed class GrainMessageSerializer
{
    /// <summary>Serializes a grain invocation request.</summary>
    public byte[] SerializeRequest(GrainInvocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SerializeRequest(request.GrainId, request.MethodId, request.ArgumentPayload.Span);
    }

    /// <summary>
    ///     Serializes a grain invocation request from its components into a single buffer,
    ///     avoiding the intermediate <c>byte[]</c> allocation that the
    ///     <see cref="GrainInvocationRequest" /> overload requires.
    /// </summary>
    public byte[] SerializeRequest(GrainId grainId, uint methodId, ReadOnlySpan<byte> argBytes)
    {
        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);
        writer.WriteString(grainId.Type.Value);
        writer.WriteString(grainId.Key);
        writer.WriteVarUInt32(methodId);
        if (argBytes.Length > 0)
        {
            writer.WriteRaw(argBytes);
        }

        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Deserializes a grain invocation request.</summary>
    public GrainInvocationRequest DeserializeRequest(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        GrainType grainType = new(reader.ReadString());
        string key = reader.ReadString();
        uint methodId = reader.ReadVarUInt32();
        ReadOnlyMemory<byte> argPayload = buffer.Slice(reader.Position);

        return new GrainInvocationRequest(new GrainId(grainType, key), methodId, argPayload);
    }

    /// <summary>Serializes a grain invocation response.</summary>
    public byte[] SerializeResponse(GrainInvocationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);
        writer.WriteByte(response.Success ? (byte)1 : (byte)0);
        writer.WriteVarUInt32((uint)response.ResultPayload.Length);
        writer.WriteRaw(response.ResultPayload.Span);
        WriteValue(writer, response.Error);
        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Deserializes a grain invocation response.</summary>
    public GrainInvocationResponse DeserializeResponse(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        bool success = reader.ReadByte() == 1;
        uint resultLen = reader.ReadVarUInt32();
        ReadOnlyMemory<byte> resultPayload = resultLen > 0
            ? buffer.Slice(reader.Position, (int)resultLen)
            : ReadOnlyMemory<byte>.Empty;
        if (resultLen > 0)
        {
            reader.ReadRaw((int)resultLen);
        }

        string? error = ReadValue(reader) as string;
        return new GrainInvocationResponse(success, resultPayload, error);
    }

    /// <summary>
    ///     Builds a serialized argument payload from boxed values.
    ///     Intended for tests and hand-written senders; generated proxies emit typed write calls directly.
    /// </summary>
    public static ReadOnlyMemory<byte> SerializeArgs(params object?[] args)
    {
        if (args.Length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);
        foreach (object? arg in args)
        {
            WriteValue(writer, arg);
        }

        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>
    ///     Reads one tagged argument value from <paramref name="reader" />.
    ///     Called by generated <c>*_TransportDispatcher</c> classes.
    /// </summary>
    public static object? ReadArg(ref CodecReader reader) => ReadValue(reader);

    public static void WriteValue(CodecWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteByte((byte)ValueKind.Null);
                break;
            case bool b:
                writer.WriteByte((byte)ValueKind.Boolean);
                writer.WriteByte(b ? (byte)1 : (byte)0);
                break;
            case int i32:
                writer.WriteByte((byte)ValueKind.Int32);
                writer.WriteInt32(i32);
                break;
            case uint u32:
                writer.WriteByte((byte)ValueKind.UInt32);
                writer.WriteVarUInt32(u32);
                break;
            case long i64:
                writer.WriteByte((byte)ValueKind.Int64);
                writer.WriteInt64(i64);
                break;
            case ulong u64:
                writer.WriteByte((byte)ValueKind.UInt64);
                writer.WriteVarUInt64(u64);
                break;
            case string s:
                writer.WriteByte((byte)ValueKind.String);
                writer.WriteString(s);
                break;
            case Guid guid:
                writer.WriteByte((byte)ValueKind.Guid);
                writer.WriteRaw(guid.ToByteArray());
                break;
            case byte[] bytes:
                writer.WriteByte((byte)ValueKind.ByteArray);
                writer.WriteBytes(bytes);
                break;
            case double dbl:
                writer.WriteByte((byte)ValueKind.Double);
                writer.WriteFixed64((ulong)BitConverter.DoubleToInt64Bits(dbl));
                break;
            case float sgl:
                writer.WriteByte((byte)ValueKind.Single);
                writer.WriteFixed32((uint)BitConverter.SingleToInt32Bits(sgl));
                break;
            case decimal dec:
                writer.WriteByte((byte)ValueKind.Decimal);
                foreach (int part in decimal.GetBits(dec))
                {
                    writer.WriteInt32(part);
                }

                break;
            case TickStatus ts:
                writer.WriteByte((byte)ValueKind.TickStatus);
                writer.WriteInt64(ts.FirstTickTime.UtcTicks);
                writer.WriteInt64(ts.Period.Ticks);
                writer.WriteInt64(ts.CurrentTickTime.UtcTicks);
                break;
            case DateTimeOffset dto:
                writer.WriteByte((byte)ValueKind.DateTimeOffset);
                writer.WriteInt64(dto.Ticks);
                writer.WriteInt64(dto.Offset.Ticks);
                break;
            case StreamId sid:
                writer.WriteByte((byte)ValueKind.StreamId);
                writer.WriteString(sid.Namespace);
                writer.WriteString(sid.Key);
                break;
            default:
                throw new NotSupportedException(
                    $"The transport message serializer does not support values of type '{value.GetType().FullName}'.");
        }
    }

    private static object? ReadValue(CodecReader reader)
    {
        var kind = (ValueKind)reader.ReadByte();
        return kind switch
        {
            ValueKind.Null => null,
            ValueKind.Boolean => reader.ReadByte() == 1,
            ValueKind.Int32 => reader.ReadInt32(),
            ValueKind.UInt32 => reader.ReadVarUInt32(),
            ValueKind.Int64 => reader.ReadInt64(),
            ValueKind.UInt64 => reader.ReadVarUInt64(),
            ValueKind.String => reader.ReadString(),
            ValueKind.Guid => new Guid(reader.ReadRaw(16)),
            ValueKind.ByteArray => reader.ReadBytes(),
            ValueKind.Double => BitConverter.Int64BitsToDouble((long)reader.ReadFixed64()),
            ValueKind.Single => BitConverter.Int32BitsToSingle((int)reader.ReadFixed32()),
            ValueKind.Decimal => new decimal([
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            ]),
            ValueKind.TickStatus => new TickStatus(
                new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero),
                TimeSpan.FromTicks(reader.ReadInt64()),
                new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero)),
            ValueKind.DateTimeOffset => new DateTimeOffset(
                reader.ReadInt64(),
                TimeSpan.FromTicks(reader.ReadInt64())),
            ValueKind.StreamId => StreamId.Create(reader.ReadString(), reader.ReadString()),
            _ => throw new NotSupportedException($"Unsupported serialized value kind '{kind}'.")
        };
    }

    private enum ValueKind : byte
    {
        Null = 0,
        Boolean = 1,
        Int32 = 2,
        UInt32 = 3,
        Int64 = 4,
        UInt64 = 5,
        String = 6,
        Guid = 7,
        ByteArray = 8,
        Double = 9,
        Single = 10,
        Decimal = 11,
        TickStatus = 12,
        DateTimeOffset = 13,
        StreamId = 14
    }
}
