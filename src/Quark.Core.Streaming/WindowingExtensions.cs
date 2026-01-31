// Copyright (c) Quark Framework. All rights reserved.

using System.Runtime.CompilerServices;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Extension methods for windowing operations on async streams.
/// </summary>
public static class WindowingExtensions
{
    /// <summary>
    /// Creates time-based windows from an async stream.
    /// Collects messages for the specified duration before emitting the window.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="duration">The duration of each window.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async stream of windows.</returns>
    public static async IAsyncEnumerable<Window<T>> Window<T>(
        this IAsyncEnumerable<T> source,
        TimeSpan duration,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");

        var messages = new List<T>();
        var startTime = DateTimeOffset.UtcNow;
        var nextWindowTime = startTime + duration;

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            
            // Check if we need to emit the current window
            if (now >= nextWindowTime)
            {
                if (messages.Count > 0)
                {
                    var endTime = nextWindowTime;
                    yield return new Window<T>(messages.ToList(), startTime, endTime, WindowType.Time);
                    messages.Clear();
                }
                
                startTime = now;
                nextWindowTime = now + duration;
            }
            
            messages.Add(item);
        }

        // Emit final window if there are remaining messages
        if (messages.Count > 0)
        {
            var endTime = DateTimeOffset.UtcNow;
            yield return new Window<T>(messages.ToList(), startTime, endTime, WindowType.Time);
        }
    }

    /// <summary>
    /// Creates count-based windows from an async stream.
    /// Collects the specified number of messages before emitting the window.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="count">The number of messages per window.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async stream of windows.</returns>
    public static async IAsyncEnumerable<Window<T>> Window<T>(
        this IAsyncEnumerable<T> source,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

        var messages = new List<T>(count);
        var startTime = DateTimeOffset.UtcNow;

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            if (messages.Count == 0)
            {
                startTime = DateTimeOffset.UtcNow;
            }

            messages.Add(item);

            if (messages.Count >= count)
            {
                var endTime = DateTimeOffset.UtcNow;
                yield return new Window<T>(messages.ToList(), startTime, endTime, WindowType.Count);
                messages.Clear();
            }
        }

        // Emit final window if there are remaining messages
        if (messages.Count > 0)
        {
            var endTime = DateTimeOffset.UtcNow;
            yield return new Window<T>(messages.ToList(), startTime, endTime, WindowType.Count);
        }
    }

    /// <summary>
    /// Creates sliding windows from an async stream.
    /// Windows overlap for continuous aggregation.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="windowSize">The size of each window.</param>
    /// <param name="slide">The number of messages to slide before creating the next window.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async stream of windows.</returns>
    public static async IAsyncEnumerable<Window<T>> SlidingWindow<T>(
        this IAsyncEnumerable<T> source,
        int windowSize,
        int slide,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be positive.");
        if (slide <= 0)
            throw new ArgumentOutOfRangeException(nameof(slide), "Slide must be positive.");

        var buffer = new List<T>();
        var messageCount = 0;

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            buffer.Add(item);
            messageCount++;

            // Emit window when we have enough messages
            if (buffer.Count >= windowSize)
            {
                var startTime = DateTimeOffset.UtcNow;
                var endTime = DateTimeOffset.UtcNow;
                yield return new Window<T>(buffer.ToList(), startTime, endTime, WindowType.Sliding);

                // Slide the window
                if (slide >= windowSize)
                {
                    buffer.Clear();
                }
                else
                {
                    buffer.RemoveRange(0, slide);
                }
            }
        }
    }

    /// <summary>
    /// Creates session windows from an async stream.
    /// Groups messages based on inactivity gaps.
    /// </summary>
    /// <typeparam name="T">The type of messages in the stream.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="inactivityGap">The inactivity gap that ends a session.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async stream of windows.</returns>
    public static async IAsyncEnumerable<Window<T>> SessionWindow<T>(
        this IAsyncEnumerable<T> source,
        TimeSpan inactivityGap,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (inactivityGap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(inactivityGap), "Inactivity gap must be positive.");

        var messages = new List<T>();
        var startTime = DateTimeOffset.UtcNow;
        var lastMessageTime = DateTimeOffset.UtcNow;

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            
            // Check if the inactivity gap has been exceeded
            if (messages.Count > 0 && (now - lastMessageTime) > inactivityGap)
            {
                // Emit the current session
                var endTime = lastMessageTime;
                yield return new Window<T>(messages.ToList(), startTime, endTime, WindowType.Session);
                messages.Clear();
                startTime = now;
            }
            else if (messages.Count == 0)
            {
                startTime = now;
            }

            messages.Add(item);
            lastMessageTime = now;
        }

        // Emit final session if there are remaining messages
        if (messages.Count > 0)
        {
            var endTime = lastMessageTime;
            yield return new Window<T>(messages.ToList(), startTime, endTime, WindowType.Session);
        }
    }
}
