namespace Quark.Streaming.Abstractions;

/// <summary>
///     Untyped observer for stream items. Allows the TCP gateway to subscribe to streams
///     without knowing the item type T at compile time.
/// </summary>
public interface IUntypedStreamObserver
{
    Task OnNextAsync(object item, StreamSequenceToken? token);
    Task OnErrorAsync(Exception ex);
    Task OnCompletedAsync();
}
