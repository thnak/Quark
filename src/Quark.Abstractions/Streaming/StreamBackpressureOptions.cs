// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Configuration options for stream backpressure and flow control.
/// Phase 8.5: Provides adaptive backpressure for slow consumers.
/// </summary>
public sealed class StreamBackpressureOptions
{
    /// <summary>
    /// Gets or sets the backpressure mode to use.
    /// Default is None (no backpressure, for backward compatibility).
    /// </summary>
    public BackpressureMode Mode { get; set; } = BackpressureMode.None;

    /// <summary>
    /// Gets or sets the maximum buffer size for pending messages.
    /// Used by DropOldest, DropNewest, and Block modes.
    /// Default is 1000 messages.
    /// </summary>
    public int BufferSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of messages per time window for throttling.
    /// Only used when Mode is Throttle.
    /// Default is 100 messages.
    /// </summary>
    public int MaxMessagesPerWindow { get; set; } = 100;

    /// <summary>
    /// Gets or sets the time window for throttling.
    /// Only used when Mode is Throttle.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan ThrottleWindow { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets whether to track backpressure metrics.
    /// When enabled, metrics like dropped messages and buffer depth are collected.
    /// Default is true.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;
}
