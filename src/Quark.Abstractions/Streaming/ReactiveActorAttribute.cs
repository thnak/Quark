// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Attribute to configure reactive actor behavior including buffer sizes and backpressure thresholds.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ReactiveActorAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the buffer size for incoming messages.
    /// Default is 1000.
    /// </summary>
    public int BufferSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the backpressure threshold as a percentage (0.0 to 1.0).
    /// When the buffer reaches this threshold, backpressure signals are sent to upstream producers.
    /// Default is 0.8 (80%).
    /// </summary>
    public double BackpressureThreshold { get; set; } = 0.8;

    /// <summary>
    /// Gets or sets the overflow strategy when buffer is full.
    /// Default is Block.
    /// </summary>
    public BackpressureMode OverflowStrategy { get; set; } = BackpressureMode.Block;

    /// <summary>
    /// Gets or sets whether to enable metrics collection for this reactive actor.
    /// Default is true.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;
}
