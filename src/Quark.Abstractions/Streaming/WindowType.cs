namespace Quark.Abstractions.Streaming;

/// <summary>
/// Defines the type of windowing strategy for grouping stream messages.
/// </summary>
public enum WindowType
{
    /// <summary>
    /// Time-based windows that collect messages for a specified duration.
    /// </summary>
    Time,

    /// <summary>
    /// Count-based windows that collect a specified number of messages.
    /// </summary>
    Count,

    /// <summary>
    /// Sliding windows that overlap for continuous aggregation.
    /// </summary>
    Sliding,

    /// <summary>
    /// Session windows that group related events based on inactivity gaps.
    /// </summary>
    Session
}