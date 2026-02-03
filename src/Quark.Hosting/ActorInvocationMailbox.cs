using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Networking.Abstractions;

namespace Quark.Hosting;

/// <summary>
/// Mailbox for processing actor method invocations sequentially.
/// Ensures turn-based, single-threaded execution per actor.
/// </summary>
internal sealed class ActorInvocationMailbox : IDisposable
{
    private readonly IActor _actor;
    private readonly IActorMethodDispatcher _dispatcher;
    private readonly IQuarkTransport _transport;
    private readonly ILogger _logger;
    private readonly Channel<ActorEnvelopeMessage> _channel;
    private readonly CancellationTokenSource _cts;

    // 0 = idle, 1 = running
    private int _isProcessing;
    private bool _isStarted;

    // Optional fairness control
    private const int MaxMessagesPerTurn = 100;

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

    public IActor Actor => _actor;
    public string ActorId => _actor.ActorId;

    public int MessageCount => _channel.Reader.Count;

    /// <summary>
    /// Enqueues a message into the mailbox.
    /// </summary>
    public bool Post(
        ActorEnvelopeMessage message)
    {
        var result = _channel.Writer.TryWrite(message);
        TrySchedule();
        return result;
    }


    /// <summary>
    /// Attempts to schedule mailbox execution.
    /// </summary>
    public void TrySchedule()
    {
        // Acquire execution token
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
            return;

        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var mailbox = (ActorInvocationMailbox)state!;
                _ = mailbox.ProcessMessagesAsync(mailbox._cts.Token);
            },
            this);
    }

    /// <summary>
    /// Stops the mailbox gracefully.
    /// </summary>
    public async Task StopAsync()
    {
        _channel.Writer.TryComplete();
        await _channel.Reader.Completion;
        if (_isStarted)
            await _actor.OnDeactivateAsync();
        await _cts.CancelAsync();
    }

    /// <summary>
    /// Main turn-based execution loop.
    /// </summary>
    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Mailbox started for actor {ActorId}", ActorId);
        if (!_isStarted)
        {
            await Actor.OnActivateAsync(cancellationToken);
            _isStarted = true;
        }

        try
        {
            int processed = 0;

            while (processed < MaxMessagesPerTurn &&
                   _channel.Reader.TryRead(out var message))
            {
                processed++;

                try
                {
                    await ProcessEnvelopeMessageAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    HandleMessageFailure(message, ex);
                }
            }

            await Task.Yield();
        }
        finally
        {
            // Release execution token
            Volatile.Write(ref _isProcessing, 0);

            // 🔑 CRITICAL: reschedule if messages arrived during execution
            if (_channel.Reader.Count != 0 && !_cts.IsCancellationRequested)
            {
                TrySchedule();
            }

            _logger.LogDebug("Mailbox turn ended for actor {ActorId}", ActorId);
        }
    }

    private async Task ProcessEnvelopeMessageAsync(
        ActorEnvelopeMessage message,
        CancellationToken cancellationToken)
    {
        var envelope = message.Envelope;

        _logger.LogTrace(
            "Processing envelope {MessageId} for actor {ActorId} ({ActorType}.{MethodName})",
            envelope.MessageId,
            envelope.ActorId,
            envelope.ActorType,
            envelope.MethodName);

        var responsePayload = await _dispatcher.InvokeAsync(
            _actor,
            envelope.MethodName,
            envelope.Payload,
            cancellationToken);

        var response = new QuarkEnvelope(
            envelope.MessageId,
            envelope.ActorId,
            envelope.ActorType,
            envelope.MethodName,
            Array.Empty<byte>(),
            envelope.CorrelationId)
        {
            ResponsePayload = responsePayload,
            IsError = false
        };

        message.SetResponse(response);
        _transport.SendResponse(response);

        _logger.LogTrace(
            "Envelope {MessageId} processed successfully",
            envelope.MessageId);
    }

    private void HandleMessageFailure(
        ActorEnvelopeMessage message,
        Exception ex)
    {
        _logger.LogError(
            ex,
            "Error processing envelope {MessageId} for actor {ActorId}",
            message.MessageId,
            ActorId);

        message.SetException(ex);

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

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }
}