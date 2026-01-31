// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Interface for reactive actors that process streams with built-in backpressure and flow control.
/// Actors implementing this interface can transform input streams to output streams with operators.
/// </summary>
/// <typeparam name="TIn">The type of input messages.</typeparam>
/// <typeparam name="TOut">The type of output messages.</typeparam>
public interface IReactiveActor<TIn, TOut>
{
    /// <summary>
    /// Processes an asynchronous stream of input messages and produces an asynchronous stream of output messages.
    /// This method is called when messages are available for processing.
    /// </summary>
    /// <param name="stream">The input stream of messages.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An asynchronous stream of output messages.</returns>
    IAsyncEnumerable<TOut> ProcessStreamAsync(
        IAsyncEnumerable<TIn> stream, 
        CancellationToken cancellationToken = default);
}
