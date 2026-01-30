using System.Collections.Concurrent;
using Quark.Abstractions;

namespace Quark.Core.Actors.Pooling;

/// <summary>
///     Factory for creating pooled actor method messages.
///     Provides a centralized access point for all message pools.
/// </summary>
public static class ActorMessageFactory
{
    private static readonly ConcurrentDictionary<Type, object> _pools = new();

    /// <summary>
    ///     Creates a pooled actor method message.
    ///     The message should be disposed after use to return it to the pool.
    /// </summary>
    /// <typeparam name="TResult">The result type of the method invocation.</typeparam>
    /// <param name="methodName">The name of the method to invoke.</param>
    /// <param name="arguments">The arguments for the method invocation.</param>
    /// <returns>A pooled message instance.</returns>
    public static PooledActorMethodMessage<TResult> CreatePooled<TResult>(string methodName, params object?[] arguments)
    {
        var pool = GetOrCreatePool<TResult>();
        return pool.Rent(methodName, arguments);
    }

    /// <summary>
    ///     Creates a standard (non-pooled) actor method message.
    ///     Use this when pooling is not appropriate (e.g., long-lived messages).
    /// </summary>
    /// <typeparam name="TResult">The result type of the method invocation.</typeparam>
    /// <param name="methodName">The name of the method to invoke.</param>
    /// <param name="arguments">The arguments for the method invocation.</param>
    /// <returns>A new message instance.</returns>
    public static ActorMethodMessage<TResult> Create<TResult>(string methodName, params object?[] arguments)
    {
        return new ActorMethodMessage<TResult>(methodName, arguments);
    }

    /// <summary>
    ///     Gets or creates a message pool for the specified result type.
    /// </summary>
    private static ActorMethodMessagePool<TResult> GetOrCreatePool<TResult>()
    {
        var pool = _pools.GetOrAdd(typeof(TResult), _ => new ActorMethodMessagePool<TResult>());
        return (ActorMethodMessagePool<TResult>)pool;
    }
}
