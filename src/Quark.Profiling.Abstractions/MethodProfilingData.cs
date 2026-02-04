namespace Quark.Profiling.Abstractions;

/// <summary>
/// Represents profiling data for a specific actor method.
/// </summary>
public sealed class MethodProfilingData
{
    /// <summary>
    /// Gets or sets the method name.
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// Gets or sets the total number of invocations.
    /// </summary>
    public long InvocationCount { get; set; }

    /// <summary>
    /// Gets or sets the total duration (milliseconds).
    /// </summary>
    public double TotalDurationMs { get; set; }

    /// <summary>
    /// Gets or sets the minimum duration (milliseconds).
    /// </summary>
    public double MinDurationMs { get; set; } = double.MaxValue;

    /// <summary>
    /// Gets or sets the maximum duration (milliseconds).
    /// </summary>
    public double MaxDurationMs { get; set; }

    /// <summary>
    /// Gets the average duration (milliseconds).
    /// </summary>
    public double AverageDurationMs =>
        InvocationCount > 0 ? TotalDurationMs / InvocationCount : 0.0;
}