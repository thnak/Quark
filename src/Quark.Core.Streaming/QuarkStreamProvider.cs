// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;
using Quark.Abstractions;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Implementation of the Quark stream provider for explicit pub/sub.
/// </summary>
public class QuarkStreamProvider : IQuarkStreamProvider
{
    private readonly ConcurrentDictionary<StreamId, object> _streams = new();
    private readonly StreamBroker _broker;

    public QuarkStreamProvider(StreamBroker broker)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public QuarkStreamProvider(IActorFactory? actorFactory = null)
        : this(new StreamBroker(actorFactory))
    {
    }

    /// <summary>
    /// Gets the underlying broker for registration purposes.
    /// </summary>
    public StreamBroker Broker => _broker;

    /// <inheritdoc/>
    public IStreamHandle<T> GetStream<T>(string @namespace, string key)
    {
        return GetStream<T>(new StreamId(@namespace, key));
    }

    /// <inheritdoc/>
    public IStreamHandle<T> GetStream<T>(StreamId streamId)
    {
        return (IStreamHandle<T>)_streams.GetOrAdd(
            streamId,
            id => new StreamHandle<T>(id, _broker));
    }
}
