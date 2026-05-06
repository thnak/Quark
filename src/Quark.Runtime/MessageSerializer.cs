using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Quark.Serialization.Abstractions;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Encodes and decodes wire-level <see cref="MessageEnvelope"/> instances.
/// </summary>
public sealed class MessageSerializer
{
    /// <summary>Serializes <paramref name="envelope"/> into a byte array.</summary>
    public byte[] Serialize(MessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);

        writer.WriteInt64(envelope.CorrelationId);
        writer.WriteByte((byte)envelope.MessageType);

        IReadOnlyDictionary<string, string> headers = envelope.Headers?.All
            ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
        writer.WriteVarUInt32((uint)headers.Count);
        foreach ((string key, string value) in headers)
        {
            writer.WriteString(key);
            writer.WriteString(value);
        }

        writer.WriteBytes(envelope.Payload.Span);
        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Deserializes a <see cref="MessageEnvelope"/> from <paramref name="buffer"/>.</summary>
    public MessageEnvelope Deserialize(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        long correlationId = reader.ReadInt64();
        MessageType messageType = (MessageType)reader.ReadByte();

        uint headerCount = reader.ReadVarUInt32();
        MessageHeaders? headers = headerCount > 0 ? new MessageHeaders() : null;
        for (uint i = 0; i < headerCount; i++)
        {
            string key = reader.ReadString();
            string value = reader.ReadString();
            headers!.Set(key, value);
        }

        byte[] payload = reader.ReadBytes();
        return new MessageEnvelope
        {
            CorrelationId = correlationId,
            MessageType = messageType,
            Headers = headers,
            Payload = payload
        };
    }

    /// <summary>Writes a length-prefixed envelope to a pipe.</summary>
    public async ValueTask WriteAsync(PipeWriter writer, MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        byte[] bytes = Serialize(envelope);
        Span<byte> prefix = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        writer.Advance(sizeof(int));

        bytes.CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the next length-prefixed envelope from a pipe, or <c>null</c> on EOF.</summary>
    public async ValueTask<MessageEnvelope?> ReadAsync(PipeReader reader, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (TryReadEnvelope(ref buffer, out MessageEnvelope? envelope))
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                return envelope;
            }

            if (result.IsCompleted)
            {
                reader.AdvanceTo(buffer.End);
                return buffer.Length == 0 ? null : throw new EndOfStreamException("Incomplete message envelope frame.");
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private bool TryReadEnvelope(ref ReadOnlySequence<byte> buffer, out MessageEnvelope? envelope)
    {
        envelope = null;

        if (buffer.Length < sizeof(int))
            return false;

        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        buffer.Slice(0, sizeof(int)).CopyTo(lengthBytes);
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        if (buffer.Length < sizeof(int) + payloadLength)
            return false;

        ReadOnlySequence<byte> payload = buffer.Slice(sizeof(int), payloadLength);
        envelope = Deserialize(payload.ToArray());
        buffer = buffer.Slice(sizeof(int) + payloadLength);
        return true;
    }
}