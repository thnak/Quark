using System.Threading.Channels;
using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
///     High-performance mailbox implementation using System.Threading.Channels.
///     Provides turn-based message processing to ensure actors process one message at a time.
/// </summary>
public sealed class ChannelMailbox : IMailbox
{
    private readonly IActor _actor;
    private readonly Channel<IActorMessage> _channel;
    private readonly CancellationTokenSource _cts;
    private int _messageCount;
    private Task? _processingTask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChannelMailbox" /> class.
    /// </summary>
    /// <param name="actor">The actor that owns this mailbox.</param>
    /// <param name="capacity">The maximum number of messages the mailbox can hold. Default is 1000.</param>
    public ChannelMailbox(IActor actor, int capacity = 1000)
    {
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _cts = new CancellationTokenSource();

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _channel = Channel.CreateBounded<IActorMessage>(options);
    }

    /// <inheritdoc />
    public string ActorId => _actor.ActorId;

    /// <inheritdoc />
    public int MessageCount => _messageCount;

    /// <inheritdoc />
    public bool IsProcessing => _processingTask != null && !_processingTask.IsCompleted;

    /// <inheritdoc />
    public async ValueTask<bool> PostAsync(IActorMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        try
        {
            await _channel.Writer.WriteAsync(message, cancellationToken);
            Interlocked.Increment(ref _messageCount);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask != null) throw new InvalidOperationException("Mailbox is already processing messages.");

        _processingTask = Task.Run(() => ProcessMessagesAsync(_cts.Token), cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _channel.Writer.Complete();

        if (_processingTask != null)
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

        _cts.Cancel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_processingTask != null && !_processingTask.IsCompleted)
        {
            _channel.Writer.Complete();
            try
            {
                _processingTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }

        _cts.Cancel();
        _cts.Dispose();
    }

    /// <summary>
    ///     Processes messages from the channel one at a time (turn-based execution).
    /// </summary>
    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
            try
            {
                await ProcessMessageAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log error but continue processing
                Console.WriteLine($"Error processing message {message.MessageId} for actor {ActorId}: {ex}");
            }
            finally
            {
                Interlocked.Decrement(ref _messageCount);
            }
    }

    /// <summary>
    ///     Processes a single message by invoking the appropriate method on the actor.
    /// </summary>
    private async Task ProcessMessageAsync(IActorMessage message, CancellationToken cancellationToken)
    {
        // For now, we just handle the basic case
        // In a full implementation, this would use reflection or source-generated code
        // to dispatch to the actual method
        if (message is IActorMethodMessage<object> methodMessage)
            try
            {
                // This is a placeholder - in reality, the source generator would create
                // dispatch code for each actor method
                var result = await InvokeMethodAsync(methodMessage, cancellationToken);
                methodMessage.CompletionSource.SetResult(result);
            }
            catch (Exception ex)
            {
                methodMessage.CompletionSource.SetException(ex);
            }
    }

    /// <summary>
    ///     Placeholder for method invocation.
    ///     In a real implementation, this would be replaced by source-generated dispatch code.
    /// </summary>
    private Task<object> InvokeMethodAsync(IActorMethodMessage<object> message, CancellationToken cancellationToken)
    {
        // This would be implemented by the source generator
        throw new NotImplementedException(
            "Method invocation should be implemented by source-generated dispatch code.");
    }
}