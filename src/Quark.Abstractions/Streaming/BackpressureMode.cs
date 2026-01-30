// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Defines the backpressure mode for handling slow consumers in streams.
/// </summary>
public enum BackpressureMode
{
    /// <summary>
    /// No backpressure - messages are delivered immediately regardless of consumer speed.
    /// This is the default behavior for backward compatibility.
    /// </summary>
    None,

    /// <summary>
    /// Drop oldest messages when the buffer is full.
    /// New messages are accepted and oldest buffered messages are discarded.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Drop newest messages when the buffer is full.
    /// New messages are rejected/dropped when buffer is at capacity.
    /// </summary>
    DropNewest,

    /// <summary>
    /// Block the publisher when the buffer is full.
    /// PublishAsync will wait until space becomes available.
    /// </summary>
    Block,

    /// <summary>
    /// Throttle message publishing based on time window.
    /// Limits the rate at which messages can be published.
    /// </summary>
    Throttle
}
