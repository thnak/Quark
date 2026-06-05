namespace Quark.Streaming.Abstractions;

/// <summary>Receives items from a stream. Drop-in equivalent of Orleans' <c>IAsyncObserver&lt;T&gt;</c>.</summary>
public interface IAsyncObserver<in T>
{
    Task OnNextAsync(T item, StreamSequenceToken? token = null);
    Task OnErrorAsync(Exception ex);
    Task OnCompletedAsync();
}
