// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Interface for implementing backpressure strategies in streams.
/// Phase 8.5: Enables pluggable flow control algorithms.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
public interface IStreamBackpressureStrategy<T>
{
    /// <summary>
    /// Gets the metrics for this backpressure strategy.
    /// </summary>
    StreamBackpressureMetrics Metrics { get; }

    /// <summary>
    /// Attempts to enqueue a message for publishing, applying backpressure if needed.
    /// </summary>
    /// <param name="message">The message to publish.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the message was accepted; false if it was dropped.</returns>
    Task<bool> TryPublishAsync(T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to dequeue the next message for delivery to subscribers.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The next message if available; null if the buffer is empty.</returns>
    Task<T?> TryDequeueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current number of pending messages in the buffer.
    /// </summary>
    int PendingCount { get; }
}
