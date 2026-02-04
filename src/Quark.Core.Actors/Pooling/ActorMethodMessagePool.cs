using System.Buffers;
using System.Collections.Concurrent;

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