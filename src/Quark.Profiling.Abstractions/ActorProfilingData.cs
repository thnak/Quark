namespace Quark.Profiling.Abstractions;

/// <summary>
/// Represents profiling data for an actor.
/// </summary>
public sealed class ActorProfilingData
{
    /// <summary>
    /// Gets or sets the actor type name.
    /// </summary>
    public required string ActorType { get; init; }

    /// <summary>
    /// Gets or sets the actor identifier.
    /// </summary>
    public required string ActorId { get; init; }

    /// <summary>
    /// Gets or sets when profiling started.
    /// </summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// Gets or sets the total number of method invocations.
    /// </summary>
    public long TotalInvocations { get; set; }

    /// <summary>
    /// Gets or sets the total time spent in method invocations (milliseconds).
    /// </summary>
    public double TotalDurationMs { get; set; }

    /// <summary>
    /// Gets or sets the minimum method invocation duration (milliseconds).
    /// </summary>
    public double MinDurationMs { get; set; } = double.MaxValue;

    /// <summary>
    /// Gets or sets the maximum method invocation duration (milliseconds).
    /// </summary>
    public double MaxDurationMs { get; set; }

    /// <summary>
    /// Gets or sets the total bytes allocated.
    /// </summary>
    public long TotalAllocations { get; set; }

    /// <summary>
    /// Gets or sets method-specific profiling data.
    /// </summary>
    public Dictionary<string, MethodProfilingData> Methods { get; set; } = new();

    /// <summary>
    /// Gets the average method invocation duration (milliseconds).
    /// </summary>
    public double AverageDurationMs =>
        TotalInvocations > 0 ? TotalDurationMs / TotalInvocations : 0.0;
}

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
