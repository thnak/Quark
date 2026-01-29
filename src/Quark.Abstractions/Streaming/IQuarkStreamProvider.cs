// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Provides access to Quark streams for explicit pub/sub scenarios.
/// This interface is used for dynamic subscriptions that change at runtime.
/// </summary>
public interface IQuarkStreamProvider
{
    /// <summary>
    /// Gets a handle to a stream for publishing and subscribing.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="namespace">The stream namespace (e.g., "orders/processed").</param>
    /// <param name="key">The stream key within the namespace.</param>
    /// <returns>A stream handle.</returns>
    IStreamHandle<T> GetStream<T>(string @namespace, string key);

    /// <summary>
    /// Gets a handle to a stream using a stream identifier.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="streamId">The stream identifier.</param>
    /// <returns>A stream handle.</returns>
    IStreamHandle<T> GetStream<T>(StreamId streamId);
}
