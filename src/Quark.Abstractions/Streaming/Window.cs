// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Represents a window of messages in a stream.
/// </summary>
/// <typeparam name="T">The type of messages in the window.</typeparam>
public sealed class Window<T>
{
    /// <summary>
    /// Gets the messages in this window.
    /// </summary>
    public IReadOnlyList<T> Messages { get; }

    /// <summary>
    /// Gets the start time of this window.
    /// </summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>
    /// Gets the end time of this window.
    /// </summary>
    public DateTimeOffset EndTime { get; }

    /// <summary>
    /// Gets the type of window.
    /// </summary>
    public WindowType Type { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Window{T}"/> class.
    /// </summary>
    /// <param name="messages">The messages in this window.</param>
    /// <param name="startTime">The start time of this window.</param>
    /// <param name="endTime">The end time of this window.</param>
    /// <param name="type">The type of window.</param>
    public Window(IReadOnlyList<T> messages, DateTimeOffset startTime, DateTimeOffset endTime, WindowType type)
    {
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        StartTime = startTime;
        EndTime = endTime;
        Type = type;
    }
}
