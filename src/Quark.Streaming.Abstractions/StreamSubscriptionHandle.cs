namespace Quark.Streaming.Abstractions;

/// <summary>
///     Handle for an active stream subscription.
///     Drop-in equivalent of Orleans' <c>StreamSubscriptionHandle&lt;T&gt;</c>.
/// </summary>
public abstract class StreamSubscriptionHandle<T>
{
    public abstract Guid HandleId { get; }
    public abstract StreamId StreamId { get; }
    public abstract Task UnsubscribeAsync();
    public abstract Task ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null);
}
