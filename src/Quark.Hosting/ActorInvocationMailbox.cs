using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Networking.Abstractions;

namespace Quark.Hosting;

/// <summary>
/// Mailbox for processing actor method invocations sequentially.
/// Ensures that each actor processes one message at a time (turn-based execution).
/// </summary>
internal sealed class ActorInvocationMailbox : IDisposable
{
    private readonly IActor _actor;
    private readonly IActorMethodDispatcher _dispatcher;
    private readonly IQuarkTransport _transport;
    private readonly ILogger _logger;
    private readonly Channel<ActorEnvelopeMessage> _channel;
    private readonly CancellationTokenSource _cts;
    private Task? _processingTask;

    public ActorInvocationMailbox(
        IActor actor,
        IActorMethodDispatcher dispatcher,
        IQuarkTransport transport,
        ILogger logger,
        int capacity = 1000)
    {
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _channel = Channel.CreateBounded<ActorEnvelopeMessage>(options);
    }

    /// <summary>
    /// Gets the actor instance this mailbox belongs to.
    /// </summary>
    public IActor Actor => _actor;

    /// <summary>
    /// Gets the actor ID this mailbox belongs to.
    /// </summary>
    public string ActorId => _actor.ActorId;

    /// <summary>
    /// Gets the current number of messages in the mailbox.
    /// </summary>
    public int MessageCount => _channel.Reader.Count;

    /// <summary>
    /// Gets whether the mailbox is currently processing messages.
    /// </summary>
    public bool IsProcessing => _processingTask != null && !_processingTask.IsCompleted;

    /// <summary>
    /// Posts an envelope message to the mailbox for sequential processing.
    /// </summary>
    public async ValueTask<bool> PostAsync(ActorEnvelopeMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        try
        {
            await _channel.Writer.WriteAsync(message, cancellationToken);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts processing messages from the mailbox.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask != null)
            throw new InvalidOperationException("Mailbox is already processing messages.");

        _processingTask = ProcessMessagesAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops processing messages from the mailbox.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Signal the channel that no more messages will be written
        _channel.Writer.Complete();

        // Wait for processing to complete
        if (_processingTask != null)
        {
            try
            {
                await _processingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            finally
            {
                _processingTask = null;
            }
        }
    }

    /// <summary>
    /// Processes messages from the mailbox sequentially.
    /// Each message is dispatched to the actor, one at a time.
    /// </summary>
    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Actor mailbox started processing for {ActorId}", ActorId);

        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ProcessEnvelopeMessageAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing envelope {MessageId} for actor {ActorId}",
                    message.MessageId, ActorId);

                // Set exception on message so response can be sent
                message.SetException(ex);

                // Send error response
                var errorResponse = new QuarkEnvelope(
                    message.Envelope.MessageId,
                    message.Envelope.ActorId,
                    message.Envelope.ActorType,
                    message.Envelope.MethodName,
                    Array.Empty<byte>(),
                    message.Envelope.CorrelationId)
                {
                    IsError = true,
                    ErrorMessage = ex.Message
                };

                _transport.SendResponse(errorResponse);
            }
        }

        _logger.LogDebug("Actor mailbox stopped processing for {ActorId}", ActorId);
    }

    /// <summary>
    /// Processes a single envelope message by invoking the actor method.
    /// </summary>
    private async Task ProcessEnvelopeMessageAsync(ActorEnvelopeMessage message, CancellationToken cancellationToken)
    {
        var envelope = message.Envelope;

        _logger.LogTrace(
            "Processing envelope {MessageId} for actor {ActorId} ({ActorType}.{MethodName})",
            envelope.MessageId, envelope.ActorId, envelope.ActorType, envelope.MethodName);

        // Invoke the method via dispatcher
        var responsePayload = await _dispatcher.InvokeAsync(
            _actor,
            envelope.MethodName,
            envelope.Payload,
            cancellationToken);

        // Create response envelope
        var response = new QuarkEnvelope(
            envelope.MessageId,
            envelope.ActorId,
            envelope.ActorType,
            envelope.MethodName,
            Array.Empty<byte>(),  // Empty payload - response data is in ResponsePayload
            envelope.CorrelationId)
        {
            ResponsePayload = responsePayload,
            IsError = false
        };

        // Set response on message
        message.SetResponse(response);

        // Send response via transport
        _transport.SendResponse(response);

        _logger.LogTrace(
            "Envelope {MessageId} processed successfully",
            envelope.MessageId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Complete the channel to signal no more messages
        _channel.Writer.Complete();
        
        // Cancel processing
        _cts.Cancel();
        
        // Don't wait for the task to complete in Dispose to avoid potential deadlocks
        // The task will complete when the channel is drained
        
        _cts.Dispose();
    }
}
