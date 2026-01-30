// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;
using Quark.Abstractions;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Implementation of the Quark stream provider for explicit pub/sub.
/// Phase 8.5: Enhanced with backpressure configuration support.
/// </summary>
public class QuarkStreamProvider : IQuarkStreamProvider
{
    private readonly ConcurrentDictionary<StreamId, object> _streams = new();
    private readonly StreamBroker _broker;
    private readonly ConcurrentDictionary<string, StreamBackpressureOptions> _backpressureConfig = new();

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

    /// <summary>
    /// Configures backpressure options for a specific stream namespace.
    /// Phase 8.5: Allows per-namespace flow control configuration.
    /// </summary>
    /// <param name="namespace">The stream namespace to configure.</param>
    /// <param name="options">The backpressure options.</param>
    public void ConfigureBackpressure(string @namespace, StreamBackpressureOptions options)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentNullException(nameof(@namespace));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        _backpressureConfig[@namespace] = options;
    }

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
            id =>
            {
                // Check if backpressure is configured for this namespace
                _backpressureConfig.TryGetValue(id.Namespace, out var options);
                return new StreamHandle<T>(id, _broker, options);
            });
    }
}
