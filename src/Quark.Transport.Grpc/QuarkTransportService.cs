using Grpc.Core;
using Microsoft.Extensions.Logging;
using Quark.Networking.Abstractions;

namespace Quark.Transport.Grpc;

/// <summary>
/// gRPC service implementation for bi-directional actor message streaming.
/// Handles incoming gRPC calls and coordinates with the transport layer.
/// </summary>
public class QuarkTransportService : QuarkTransport.QuarkTransportBase
{
    private readonly IEnvelopeReceiver? _envelopeReceiver;
    private readonly ILogger<QuarkTransportService> _logger;
    private readonly IQuarkChannelEnvelopeQueue _quarkChannelEnvelopeQueue;

    public QuarkTransportService(IQuarkTransport transport, ILogger<QuarkTransportService> logger,
        IQuarkChannelEnvelopeQueue quarkChannelEnvelopeQueue)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _quarkChannelEnvelopeQueue = quarkChannelEnvelopeQueue;
        
        // Try to get IEnvelopeReceiver from transport
        _envelopeReceiver = transport as IEnvelopeReceiver;
        
        // Log a warning if transport doesn't implement IEnvelopeReceiver
        if (_envelopeReceiver == null)
        {
            _logger.LogWarning("Transport does not implement IEnvelopeReceiver. Envelope reception will not be processed.");
        }
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
                    await responseStream.WriteAsync(EnvelopeMessageConverter.ToProtoMessage(message));
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
                    var envelope = EnvelopeMessageConverter.FromProtoMessage(message);

                    // Notify transport layer of received envelope using interface
                    _envelopeReceiver?.OnEnvelopeReceived(envelope);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing incoming message from {Peer}", clientPeer);
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
}