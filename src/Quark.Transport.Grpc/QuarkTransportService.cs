using Grpc.Core;
using Microsoft.Extensions.Logging;
using Quark.Networking.Abstractions;

namespace Quark.Transport.Grpc;

public class QuarkTransportService : QuarkTransport.QuarkTransportBase
{
    private readonly IQuarkTransport _transport;
    private readonly ILogger<QuarkTransportService> _logger;
    private readonly IQuarkChannelEnvelopeQueue _quarkChannelEnvelopeQueue;

    public QuarkTransportService(IQuarkTransport transport, ILogger<QuarkTransportService> logger,
        IQuarkChannelEnvelopeQueue quarkChannelEnvelopeQueue)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _quarkChannelEnvelopeQueue = quarkChannelEnvelopeQueue;
    }

    public override async Task ActorStream(
        IAsyncStreamReader<EnvelopeMessage> requestStream,
        IServerStreamWriter<EnvelopeMessage> responseStream,
        ServerCallContext context)
    {
        var clientPeer = context.Peer;
        _logger.LogInformation("ActorStream connection established from {Peer}", clientPeer);

        // 1. The Writing Loop: Reads from the channel and writes to gRPC
        var writeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in _quarkChannelEnvelopeQueue.Outgoing.Reader.ReadAllAsync(
                                   context.CancellationToken))
                {
                    message.ResponsePayload ??= [];
                    await responseStream.WriteAsync(ToProtoMessage(message));
                    _logger.LogTrace("Response sent for message {MessageId}", message.MessageId);
                }
            }
            catch (OperationCanceledException)
            {
                /* Normal shutdown */
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in gRPC writing loop for {Peer}", clientPeer);
            }
        });

        
        try
        {
            // 3. The Reading Loop: Processes incoming requests from the client
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                try
                {
                    var envelope = FromProtoMessage(message);

                    if (_transport is GrpcQuarkTransport grpcTransport)
                    {
                        grpcTransport.RaiseEnvelopeReceived(envelope);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing incoming message");
                    _quarkChannelEnvelopeQueue.Outgoing.Writer.TryWrite(FromProtoMessage(message));
                }
            }
        }
        finally
        {
            // Cleanup
            await writeTask; // Wait for the writer to finish draining
            _logger.LogInformation("ActorStream connection closed from {Peer}", clientPeer);
        }
    }

    private static EnvelopeMessage ToProtoMessage(QuarkEnvelope envelope)
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

    private static QuarkEnvelope FromProtoMessage(EnvelopeMessage message)
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