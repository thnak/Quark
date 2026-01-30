// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Represents a handle to a stream for publishing and subscribing to messages.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
public interface IStreamHandle<T>
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    StreamId StreamId { get; }

    /// <summary>
    /// Publishes a message to the stream.
    /// </summary>
    /// <param name="message">The message to publish.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishAsync(T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to the stream with a callback function.
    /// </summary>
    /// <param name="onNext">The callback to invoke when a message is received.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A subscription handle that can be used to unsubscribe.</returns>
    Task<IStreamSubscriptionHandle> SubscribeAsync(
        Func<T, Task> onNext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the backpressure metrics for this stream.
    /// Phase 8.5: Provides visibility into flow control behavior.
    /// </summary>
    StreamBackpressureMetrics? BackpressureMetrics { get; }
}
