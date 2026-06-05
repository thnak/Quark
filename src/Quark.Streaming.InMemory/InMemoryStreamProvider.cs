using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

public sealed class InMemoryStreamProvider : IStreamProvider
{
    private readonly ConcurrentDictionary<(StreamId StreamId, Type ElementType), object> _streams = new();
    private readonly StreamSubscriptionRegistry _registry = new();

    public InMemoryStreamProvider(string name) => Name = name;

    public string Name { get; }

    public IAsyncStream<T> GetStream<T>(StreamId streamId)
        => (IAsyncStream<T>)_streams.GetOrAdd((streamId, typeof(T)), _ => new InMemoryStream<T>(streamId, _registry));
}
