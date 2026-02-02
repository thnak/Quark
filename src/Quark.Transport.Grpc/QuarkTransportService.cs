using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Quark.Networking.Abstractions;

namespace Quark.Transport.Grpc;

public class QuarkTransportService : QuarkTransport.QuarkTransportBase
{
    private readonly IQuarkTransport _transport;
    private readonly ILogger<QuarkTransportService> _logger;

    public QuarkTransportService(IQuarkTransport transport, ILogger<QuarkTransportService> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task ActorStream(
        IAsyncStreamReader<EnvelopeMessage> requestStream,
        IServerStreamWriter<EnvelopeMessage> responseStream,
        ServerCallContext context)
    {
        var clientPeer = context.Peer;
        _logger.LogInformation("ActorStream connection established from {Peer}", clientPeer);

        // Create a channel to queue outgoing messages. 
        // This ensures thread-safe, sequential writes to the gRPC stream.
        var outgoingMessages = Channel.CreateUnbounded<EnvelopeMessage>(new UnboundedChannelOptions
        {
            SingleReader = true, // Only the writing loop will read
            SingleWriter = false // Many actors might push responses
        });

        // 1. The Writing Loop: Reads from the channel and writes to gRPC
        var writeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in outgoingMessages.Reader.ReadAllAsync(context.CancellationToken))
                {
                    message.ResponsePayload ??= Google.Protobuf.ByteString.Empty;
                    await responseStream.WriteAsync(message);
                    _logger.LogTrace("Response sent for message {MessageId}", message.MessageId);
                }
            }
            catch (OperationCanceledException) { /* Normal shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in gRPC writing loop for {Peer}", clientPeer);
            }
        });

        // 2. The Event Handler: Pushes responses into the local channel
        void OnEnvelopeReceived(object? sender, QuarkEnvelope envelope)
        {
            // Only handle messages that have a response payload or are errors
            // Filter out incoming requests to prevent echo loop - only send actual responses
            if (envelope.ResponsePayload == null && !envelope.IsError)
            {
                // This is an incoming request, not a response - don't echo it back
                return;
            }
            
            var protoMessage = ToProtoMessage(envelope);
            if (!outgoingMessages.Writer.TryWrite(protoMessage))
            {
                _logger.LogWarning("Failed to queue response for {MessageId}", envelope.MessageId);
            }
        }

        _transport.EnvelopeReceived += OnEnvelopeReceived;

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
                    outgoingMessages.Writer.TryWrite(new EnvelopeMessage
                    {
                        MessageId = message.MessageId,
                        IsError = true,
                        ErrorMessage = ex.Message
                    });
                }
            }
        }
        finally
        {
            // Cleanup
            _transport.EnvelopeReceived -= OnEnvelopeReceived;
            outgoingMessages.Writer.TryComplete();
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