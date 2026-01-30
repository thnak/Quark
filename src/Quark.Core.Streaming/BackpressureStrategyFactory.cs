// Copyright (c) Quark Framework. All rights reserved.

using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Factory for creating backpressure strategies.
/// Phase 8.5: Provides pluggable flow control implementations.
/// </summary>
internal static class BackpressureStrategyFactory
{
    /// <summary>
    /// Creates a backpressure strategy based on the specified options.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="options">The backpressure configuration options.</param>
    /// <returns>A backpressure strategy implementation.</returns>
    public static IStreamBackpressureStrategy<T> Create<T>(StreamBackpressureOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        return options.Mode switch
        {
            BackpressureMode.None => new NoBackpressureStrategy<T>(),
            BackpressureMode.DropOldest => new DropOldestStrategy<T>(options),
            BackpressureMode.DropNewest => new DropNewestStrategy<T>(options),
            BackpressureMode.Block => new BlockStrategy<T>(options),
            BackpressureMode.Throttle => new ThrottleStrategy<T>(options),
            _ => throw new ArgumentException($"Unsupported backpressure mode: {options.Mode}", nameof(options))
        };
    }
}
