using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
///     Phase 8.3: Adaptive mailbox with burst handling capabilities.
///     - Dynamically adjusts capacity based on load
///     - Integrated circuit breaker for fault tolerance
///     - Rate limiting for traffic control
///     - Maintains backward compatibility (adaptive features disabled by default)
/// </summary>
public sealed class AdaptiveMailbox : IMailbox
{
    private readonly IActor _actor;
    private readonly CancellationTokenSource _cts;
    private readonly IDeadLetterQueue? _deadLetterQueue;
    private readonly AdaptiveMailboxOptions _adaptiveOptions;
    private readonly CircuitBreakerOptions _circuitBreakerOptions;
    private readonly RateLimitOptions _rateLimitOptions;

    private Channel<IActorMessage> _channel;
    private Task? _processingTask;

    // Adaptive sizing state
    private int _currentCapacity;
    private readonly List<double> _utilizationSamples = new();
    private readonly object _adaptLock = new();

    // Circuit breaker state
    private CircuitState _circuitState = CircuitState.Closed;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private DateTimeOffset _circuitOpenedAt;
    private readonly Queue<DateTimeOffset> _recentFailures = new();

    // Rate limiting state
    private readonly Queue<DateTimeOffset> _messageTimestamps = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdaptiveMailbox" /> class.
    /// </summary>
    /// <param name="actor">The actor that owns this mailbox.</param>
    /// <param name="adaptiveOptions">Options for adaptive mailbox sizing.</param>
    /// <param name="circuitBreakerOptions">Options for circuit breaker.</param>
    /// <param name="rateLimitOptions">Options for rate limiting.</param>
    /// <param name="deadLetterQueue">Optional dead letter queue for capturing failed messages.</param>
    public AdaptiveMailbox(
        IActor actor,
        AdaptiveMailboxOptions? adaptiveOptions = null,
        CircuitBreakerOptions? circuitBreakerOptions = null,
        RateLimitOptions? rateLimitOptions = null,
        IDeadLetterQueue? deadLetterQueue = null)
    {
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _cts = new CancellationTokenSource();
        _deadLetterQueue = deadLetterQueue;

        _adaptiveOptions = adaptiveOptions ?? new AdaptiveMailboxOptions();
        _circuitBreakerOptions = circuitBreakerOptions ?? new CircuitBreakerOptions();
        _rateLimitOptions = rateLimitOptions ?? new RateLimitOptions();

        _currentCapacity = _adaptiveOptions.InitialCapacity;
        _channel = CreateChannel(_currentCapacity);
    }

    /// <inheritdoc />
    public string ActorId => _actor.ActorId;

    /// <inheritdoc />
    public int MessageCount => _channel.Reader.Count;

    /// <inheritdoc />
    public bool IsProcessing => _processingTask != null && !_processingTask.IsCompleted;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> PostAsync(IActorMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        // Check circuit breaker
        if (_circuitBreakerOptions.Enabled && !CanProcessMessage())
        {
            return false; // Circuit is open, reject message
        }

        // Check rate limit
        if (_rateLimitOptions.Enabled && !CheckRateLimit())
        {
            if (_rateLimitOptions.ExcessAction == RateLimitAction.Reject)
            {
                throw new InvalidOperationException($"Rate limit exceeded for actor {ActorId}");
            }
            if (_rateLimitOptions.ExcessAction == RateLimitAction.Drop)
            {
                return false; // Drop message
            }
            // Queue action: continue to post (subject to mailbox capacity)
        }

        try
        {
            await _channel.Writer.WriteAsync(message, cancellationToken);

            // Sample utilization for adaptive sizing
            if (_adaptiveOptions.Enabled)
            {
                SampleUtilization();
            }

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
        if (_processingTask != null)
            throw new InvalidOperationException("Mailbox is already processing messages.");

        _processingTask = Task.Run(() => ProcessMessagesAsync(_cts.Token), cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _channel.Writer.Complete();

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

        _cts.Cancel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Only complete the channel if it hasn't been completed yet
        if (_processingTask != null && !_processingTask.IsCompleted)
        {
            try
            {
                _channel.Writer.Complete();
            }
            catch (ChannelClosedException)
            {
                // Channel already closed, ignore
            }

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

    private Channel<IActorMessage> CreateChannel(int capacity)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        return Channel.CreateBounded<IActorMessage>(options);
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ProcessMessageAsync(message, cancellationToken);

                // Record success for circuit breaker
                if (_circuitBreakerOptions.Enabled)
                {
                    RecordSuccess();
                }
            }
            catch (Exception ex)
            {
                // Log error but continue processing
                Console.WriteLine($"Error processing message {message.MessageId} for actor {ActorId}: {ex}");

                // Record failure for circuit breaker
                if (_circuitBreakerOptions.Enabled)
                {
                    RecordFailure();
                }

                // Enqueue to DLQ
                if (_deadLetterQueue != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _deadLetterQueue.EnqueueAsync(message, ActorId, ex, CancellationToken.None);
                        }
                        catch (Exception dlqEx)
                        {
                            Console.WriteLine($"Failed to enqueue message to DLQ: {dlqEx}");
                        }
                    }, CancellationToken.None);
                }
            }
        }
    }

    private async Task ProcessMessageAsync(IActorMessage message, CancellationToken cancellationToken)
    {
        if (message is IActorMethodMessage<object> methodMessage)
        {
            try
            {
                var result = await InvokeMethodAsync(methodMessage, cancellationToken);
                methodMessage.CompletionSource.SetResult(result);
            }
            catch (Exception ex)
            {
                methodMessage.CompletionSource.SetException(ex);
                throw;
            }
        }
    }

    private Task<object> InvokeMethodAsync(IActorMethodMessage<object> message, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(
            "Method invocation should be implemented by source-generated dispatch code.");
    }

    // Phase 8.3: Adaptive sizing logic
    private void SampleUtilization()
    {
        var utilization = (double)MessageCount / _currentCapacity;

        lock (_adaptLock)
        {
            _utilizationSamples.Add(utilization);

            if (_utilizationSamples.Count >= _adaptiveOptions.MinSamplesBeforeAdapt)
            {
                var avgUtilization = _utilizationSamples.Average();
                _utilizationSamples.Clear();

                // Check if we should grow
                if (avgUtilization >= _adaptiveOptions.GrowThreshold &&
                    _currentCapacity < _adaptiveOptions.MaxCapacity)
                {
                    ResizeMailbox((int)(_currentCapacity * _adaptiveOptions.GrowthFactor));
                }
                // Check if we should shrink
                else if (avgUtilization <= _adaptiveOptions.ShrinkThreshold &&
                         _currentCapacity > _adaptiveOptions.MinCapacity)
                {
                    ResizeMailbox((int)(_currentCapacity * _adaptiveOptions.ShrinkFactor));
                }
            }
        }
    }

    private void ResizeMailbox(int newCapacity)
    {
        // Clamp to min/max
        newCapacity = Math.Max(_adaptiveOptions.MinCapacity,
            Math.Min(_adaptiveOptions.MaxCapacity, newCapacity));

        if (newCapacity == _currentCapacity)
            return;

        // Create new channel with new capacity
        var oldChannel = _channel;
        _channel = CreateChannel(newCapacity);
        _currentCapacity = newCapacity;

        // Note: In a production implementation, you would need to drain the old channel
        // and transfer messages to the new one. For simplicity, we just switch channels.
        // Messages in the old channel will be processed when the old channel is read.
    }

    // Phase 8.3: Circuit breaker logic
    private bool CanProcessMessage()
    {
        CleanupOldFailures();

        switch (_circuitState)
        {
            case CircuitState.Open:
                // Check if timeout has elapsed
                if (DateTimeOffset.UtcNow - _circuitOpenedAt >= _circuitBreakerOptions.Timeout)
                {
                    _circuitState = CircuitState.HalfOpen;
                    _consecutiveSuccesses = 0;
                    return true;
                }
                return false;

            case CircuitState.HalfOpen:
            case CircuitState.Closed:
            default:
                return true;
        }
    }

    private void RecordSuccess()
    {
        switch (_circuitState)
        {
            case CircuitState.HalfOpen:
                _consecutiveSuccesses++;
                if (_consecutiveSuccesses >= _circuitBreakerOptions.SuccessThreshold)
                {
                    _circuitState = CircuitState.Closed;
                    _consecutiveFailures = 0;
                    _consecutiveSuccesses = 0;
                }
                break;

            case CircuitState.Closed:
                _consecutiveFailures = 0;
                break;
        }
    }

    private void RecordFailure()
    {
        _recentFailures.Enqueue(DateTimeOffset.UtcNow);
        CleanupOldFailures();

        switch (_circuitState)
        {
            case CircuitState.HalfOpen:
                // Any failure in half-open state reopens the circuit
                _circuitState = CircuitState.Open;
                _circuitOpenedAt = DateTimeOffset.UtcNow;
                _consecutiveSuccesses = 0;
                break;

            case CircuitState.Closed:
                _consecutiveFailures++;
                if (_consecutiveFailures >= _circuitBreakerOptions.FailureThreshold)
                {
                    _circuitState = CircuitState.Open;
                    _circuitOpenedAt = DateTimeOffset.UtcNow;
                }
                break;
        }
    }

    private void CleanupOldFailures()
    {
        var cutoff = DateTimeOffset.UtcNow - _circuitBreakerOptions.SamplingWindow;
        while (_recentFailures.Count > 0 && _recentFailures.Peek() < cutoff)
        {
            _recentFailures.Dequeue();
        }
    }

    // Phase 8.3: Rate limiting logic
    private bool CheckRateLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - _rateLimitOptions.TimeWindow;

        // Remove old timestamps
        while (_messageTimestamps.Count > 0 && _messageTimestamps.Peek() < windowStart)
        {
            _messageTimestamps.Dequeue();
        }

        // Check if we're within limit
        if (_messageTimestamps.Count >= _rateLimitOptions.MaxMessagesPerWindow)
        {
            return false;
        }

        _messageTimestamps.Enqueue(now);
        return true;
    }
}
