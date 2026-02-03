using Quark.Networking.Abstractions;

namespace Quark.Transport.Grpc;

/// <summary>
/// Utility class for converting between QuarkEnvelope and EnvelopeMessage (protobuf).
/// Centralizes conversion logic to avoid duplication across transport components.
/// </summary>
internal static class EnvelopeMessageConverter
{
    /// <summary>
    /// Converts a QuarkEnvelope to a protobuf EnvelopeMessage.
    /// </summary>
    /// <param name="envelope">The envelope to convert.</param>
    /// <returns>The converted protobuf message.</returns>
    public static EnvelopeMessage ToProtoMessage(QuarkEnvelope envelope)
    {
        return new EnvelopeMessage
        {
            MessageId = envelope.MessageId,
            ActorId = envelope.ActorId ?? string.Empty,
            ActorType = envelope.ActorType ?? string.Empty,
            MethodName = envelope.MethodName ?? string.Empty,
            Payload = Google.Protobuf.ByteString.CopyFrom(envelope.Payload ?? Array.Empty<byte>()),
            CorrelationId = envelope.CorrelationId,
            Timestamp = envelope.Timestamp.ToUnixTimeMilliseconds(),
            ResponsePayload = envelope.ResponsePayload != null
                ? Google.Protobuf.ByteString.CopyFrom(envelope.ResponsePayload)
                : Google.Protobuf.ByteString.Empty,
            IsError = envelope.IsError,
            ErrorMessage = envelope.ErrorMessage ?? string.Empty
        };
    }

    /// <summary>
    /// Converts a protobuf EnvelopeMessage to a QuarkEnvelope.
    /// </summary>
    /// <param name="message">The protobuf message to convert.</param>
    /// <returns>The converted envelope.</returns>
    public static QuarkEnvelope FromProtoMessage(EnvelopeMessage message)
    {
        return new QuarkEnvelope(
            message.MessageId,
            message.ActorId,
            message.ActorType,
            message.MethodName,
            message.Payload.ToByteArray(),
            message.CorrelationId)
        {
            ResponsePayload = message.ResponsePayload.Length > 0 ? message.ResponsePayload.ToByteArray() : null,
            IsError = message.IsError,
            ErrorMessage = message.ErrorMessage
        };
    }
}
