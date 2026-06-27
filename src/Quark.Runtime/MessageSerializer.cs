using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Encodes and decodes wire-level <see cref="MessageEnvelope" /> instances.
/// </summary>
public sealed class MessageSerializer
{
    /// <summary>Default maximum number of headers accepted on an envelope.</summary>
    public const uint DefaultMaxHeaders = 1000;

    private readonly int _maxMessageBytes;
    private readonly uint _maxHeaders;

    /// <summary>Initializes a new serializer using default transport limits.</summary>
    public MessageSerializer()
        : this(new TransportOptions())
    {
    }

    /// <summary>Initializes a new serializer using the provided transport limits.</summary>
    public MessageSerializer(TransportOptions options, uint maxHeaders = DefaultMaxHeaders)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxMessageBytes);

        _maxMessageBytes = options.MaxMessageBytes;
        _maxHeaders = maxHeaders;
    }

    /// <summary>Serializes <paramref name="envelope" /> into a byte array.</summary>
    public byte[] Serialize(MessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);

        writer.WriteInt64(envelope.CorrelationId);
        writer.WriteByte((byte)envelope.MessageType);

        IReadOnlyDictionary<string, string> headers = envelope.Headers?.All
                                                      ?? new Dictionary<string, string>();
        writer.WriteVarUInt32((uint)headers.Count);
        foreach ((string key, string value) in headers)
        {
            writer.WriteString(key);
            writer.WriteString(value);
        }

        writer.WriteBytes(envelope.Payload.Span);
        byte[] bytes = buffer.WrittenMemory.ToArray();
        if (bytes.Length > _maxMessageBytes)
        {
            throw new InvalidDataException(
                $"Serialized message size {bytes.Length} exceeds the configured maximum of {_maxMessageBytes} bytes.");
        }

        return bytes;
    }

    /// <summary>Deserializes a <see cref="MessageEnvelope" /> from <paramref name="buffer" />.</summary>
    public MessageEnvelope Deserialize(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        long correlationId = reader.ReadInt64();
        var messageType = (MessageType)reader.ReadByte();

        uint headerCount = reader.ReadVarUInt32();
        if (headerCount > _maxHeaders)
        {
            throw new InvalidDataException(
                $"Header count {headerCount} exceeds the configured maximum of {_maxHeaders}.");
        }

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
    public async ValueTask WriteAsync(PipeWriter writer, MessageEnvelope envelope,
        CancellationToken cancellationToken = default)
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
                // Mark only the consumed frame as examined (examined == consumed). Using
                // buffer.End here would tell the PipeReader we examined ALL buffered bytes, so the
                // next ReadAsync would block for new data even when a second, already-buffered
                // frame is sitting in the pipe. That stalls the trailing frame whenever two
                // envelopes coalesce into one read — e.g. an ObserverInvoke immediately followed
                // by its grain-call Response on the gateway back-channel (issue #49).
                reader.AdvanceTo(buffer.Start);
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
        {
            return false;
        }

        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        buffer.Slice(0, sizeof(int)).CopyTo(lengthBytes);
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        if (payloadLength < 0)
        {
            throw new InvalidDataException("Message frame length cannot be negative.");
        }

        if (payloadLength > _maxMessageBytes)
        {
            throw new InvalidDataException(
                $"Message frame length {payloadLength} exceeds the configured maximum of {_maxMessageBytes} bytes.");
        }

        long frameLength = sizeof(int) + (long)payloadLength;
        if (buffer.Length < frameLength)
        {
            return false;
        }

        ReadOnlySequence<byte> payload = buffer.Slice(sizeof(int), payloadLength);
        envelope = Deserialize(payload.ToArray());
        buffer = buffer.Slice(frameLength);
        return true;
    }
}
