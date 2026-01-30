using System.Buffers;
using System.Collections.Concurrent;
using Quark.Abstractions;

namespace Quark.Core.Actors.Pooling;

/// <summary>
///     Object pool for <see cref="ActorMethodMessage{TResult}" /> instances.
///     Reduces allocations in the actor messaging hot path by reusing message objects.
/// </summary>
/// <typeparam name="TResult">The result type of the method invocation.</typeparam>
public sealed class ActorMethodMessagePool<TResult>
{
    private readonly ConcurrentBag<PooledActorMethodMessage<TResult>> _pool = new();
    private readonly TaskCompletionSourcePool<TResult> _tcsPool = new();
    private int _count;
    private readonly int _maxPoolSize;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorMethodMessagePool{TResult}" /> class.
    /// </summary>
    /// <param name="maxPoolSize">The maximum number of objects to keep in the pool. Default is 1024.</param>
    public ActorMethodMessagePool(int maxPoolSize = 1024)
    {
        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    ///     Rents a pooled actor method message from the pool.
    /// </summary>
    /// <param name="methodName">The name of the method to invoke.</param>
    /// <param name="arguments">The arguments for the method invocation.</param>
    /// <returns>A pooled message instance ready for use.</returns>
    public PooledActorMethodMessage<TResult> Rent(string methodName, object?[] arguments)
    {
        PooledActorMethodMessage<TResult> message;
        
        if (_pool.TryTake(out message!))
        {
            Interlocked.Decrement(ref _count);
            message.Reset(methodName, arguments, _tcsPool.Rent());
        }
        else
        {
            message = new PooledActorMethodMessage<TResult>(this, methodName, arguments, _tcsPool.Rent());
        }

        return message;
    }

    /// <summary>
    ///     Returns a pooled message to the pool for reuse.
    /// </summary>
    /// <param name="message">The message to return to the pool.</param>
    internal void Return(PooledActorMethodMessage<TResult> message)
    {
        if (message == null)
            return;

        // Return the TCS to its pool
        if (message.CompletionSource.Task.IsCompleted)
        {
            _tcsPool.Return(message.CompletionSource);
        }

        // Don't exceed max pool size
        if (_count >= _maxPoolSize)
            return;

        _pool.Add(message);
        Interlocked.Increment(ref _count);
    }

    /// <summary>
    ///     Gets the current number of objects in the pool.
    /// </summary>
    public int Count => _count;
}

/// <summary>
///     A pooled actor method message that can be reused to reduce allocations.
/// </summary>
/// <typeparam name="TResult">The result type of the method invocation.</typeparam>
public sealed class PooledActorMethodMessage<TResult> : IActorMethodMessage<TResult>, IDisposable
{
    private readonly ActorMethodMessagePool<TResult> _pool;
    private string? _messageId;
    private string? _methodName;
    private object?[]? _arguments;
    private TaskCompletionSource<TResult>? _completionSource;
    private bool _disposed;

    internal PooledActorMethodMessage(
        ActorMethodMessagePool<TResult> pool,
        string methodName,
        object?[] arguments,
        TaskCompletionSource<TResult> completionSource)
    {
        _pool = pool;
        Reset(methodName, arguments, completionSource);
    }

    internal void Reset(string methodName, object?[] arguments, TaskCompletionSource<TResult> completionSource)
    {
        _messageId = MessageIdGenerator.Generate();
        _methodName = methodName;
        _arguments = arguments ?? Array.Empty<object?>();
        _completionSource = completionSource;
        CorrelationId = null;
        Timestamp = DateTimeOffset.UtcNow;
        _disposed = false;
    }

    /// <inheritdoc />
    public string MessageId => _messageId!;

    /// <inheritdoc />
    public string? CorrelationId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; private set; }

    /// <inheritdoc />
    public string MethodName => _methodName!;

    /// <inheritdoc />
    public object?[] Arguments => _arguments!;

    /// <inheritdoc />
    public TaskCompletionSource<TResult> CompletionSource => _completionSource!;

    /// <summary>
    ///     Returns this message to the pool for reuse.
    ///     Should be called after the message has been fully processed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pool.Return(this);
    }
}
