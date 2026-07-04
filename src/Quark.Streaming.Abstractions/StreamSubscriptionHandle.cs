namespace Quark.Streaming.Abstractions;

/// <summary>
///     Handle for an active stream subscription.
///     Drop-in equivalent of Orleans' <c>StreamSubscriptionHandle&lt;T&gt;</c>.
/// </summary>
public abstract class StreamSubscriptionHandle<T>
{
    public abstract Guid HandleId { get; }// TODO did not implemented or used in any elsewhere
    public abstract StreamId StreamId { get; }// TODO did not implemented or used in any elsewhere
    public abstract Task UnsubscribeAsync();
    public abstract Task ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null);// TODO did not implemented or used in any elsewhere
}
