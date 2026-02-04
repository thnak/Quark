using Quark.Abstractions;

namespace Quark.Core.Actors.Pooling;

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