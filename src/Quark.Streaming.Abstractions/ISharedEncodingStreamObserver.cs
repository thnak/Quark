namespace Quark.Streaming.Abstractions;

/// <summary>
///     Optional extension of <see cref="IUntypedStreamObserver" /> for observers that serialize the
///     stream item. Fan-out routes through <see cref="OnNextSharedAsync" /> so encoding is shared
///     across all such observers subscribed to the same stream.
/// </summary>
public interface ISharedEncodingStreamObserver : IUntypedStreamObserver
{
    /// <summary>
    ///     Delivers <paramref name="item" />, encoding it at most once across all shared-encoding
    ///     observers via <see cref="SharedStreamItem.GetOrEncode" />.
    /// </summary>
    Task OnNextSharedAsync(SharedStreamItem item, StreamSequenceToken? token);
}
