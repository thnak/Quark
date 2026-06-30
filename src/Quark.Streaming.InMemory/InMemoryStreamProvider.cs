using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

public sealed class InMemoryStreamProvider : IStreamProvider
{
    private readonly ConcurrentDictionary<(StreamId StreamId, Type ElementType), object> _streams = new();
    private readonly StreamSubscriptionRegistry _registry;

    public InMemoryStreamProvider(string name, StreamSubscriptionRegistry registry)
    {
        Name = name;
        _registry = registry;
    }

    public string Name { get; }

    public IAsyncStream<T> GetStream<T>(StreamId streamId)
    {
        return (IAsyncStream<T>)_streams.GetOrAdd((streamId, typeof(T)), ValueFactory);

        object ValueFactory((StreamId StreamId, Type ElementType) _) => new InMemoryStream<T>(streamId, _registry);
    }
}
