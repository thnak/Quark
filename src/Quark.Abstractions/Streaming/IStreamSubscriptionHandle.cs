// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Represents a handle to a stream subscription that can be used to unsubscribe.
/// </summary>
public interface IStreamSubscriptionHandle : IDisposable
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    StreamId StreamId { get; }

    /// <summary>
    /// Gets whether this subscription is still active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Unsubscribes from the stream.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UnsubscribeAsync(CancellationToken cancellationToken = default);
}
