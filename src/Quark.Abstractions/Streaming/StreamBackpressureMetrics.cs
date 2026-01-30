// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Metrics for monitoring backpressure behavior in streams.
/// Phase 8.5: Provides visibility into flow control effectiveness.
/// </summary>
public sealed class StreamBackpressureMetrics
{
    /// <summary>
    /// Gets the total number of messages successfully published.
    /// </summary>
    public long MessagesPublished { get; set; }

    /// <summary>
    /// Gets the total number of messages dropped due to backpressure.
    /// </summary>
    public long MessagesDropped { get; set; }

    /// <summary>
    /// Gets the total number of times publishing was throttled or blocked.
    /// </summary>
    public long ThrottleEvents { get; set; }

    /// <summary>
    /// Gets the current depth of the pending message buffer.
    /// </summary>
    public int CurrentBufferDepth { get; set; }

    /// <summary>
    /// Gets the peak buffer depth observed.
    /// </summary>
    public int PeakBufferDepth { get; set; }

    /// <summary>
    /// Gets the timestamp of the last metric update.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Resets all metrics to their initial values.
    /// </summary>
    public void Reset()
    {
        MessagesPublished = 0;
        MessagesDropped = 0;
        ThrottleEvents = 0;
        CurrentBufferDepth = 0;
        PeakBufferDepth = 0;
        LastUpdated = DateTimeOffset.UtcNow;
    }
}
