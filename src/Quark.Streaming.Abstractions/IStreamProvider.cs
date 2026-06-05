namespace Quark.Streaming.Abstractions;

/// <summary>
///     Factory for obtaining stream instances.
///     Drop-in equivalent of Orleans' <c>IStreamProvider</c>.
/// </summary>
public interface IStreamProvider
{
    string Name { get; }
    IAsyncStream<T> GetStream<T>(StreamId streamId);
}
