// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Interface for actors that can receive stream messages.
/// Actors that use the [QuarkStream] attribute should implement this interface.
/// </summary>
/// <typeparam name="T">The type of messages the actor can consume from the stream.</typeparam>
public interface IStreamConsumer<T>
{
    /// <summary>
    /// Called when a stream message is received.
    /// </summary>
    /// <param name="message">The message received from the stream.</param>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnStreamMessageAsync(T message, StreamId streamId, CancellationToken cancellationToken = default);
}
