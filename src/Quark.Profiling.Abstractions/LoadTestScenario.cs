namespace Quark.Profiling.Abstractions;

/// <summary>
/// Defines a load test scenario.
/// </summary>
public sealed class LoadTestScenario
{
    /// <summary>
    /// Gets or sets the unique test identifier.
    /// </summary>
    public string TestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the actor type to test.
    /// </summary>
    public required string ActorType { get; init; }

    /// <summary>
    /// Gets or sets the number of concurrent actors.
    /// </summary>
    public int ConcurrentActors { get; set; } = 100;

    /// <summary>
    /// Gets or sets the number of messages per actor.
    /// </summary>
    public int MessagesPerActor { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the test duration in seconds (0 for message count based).
    /// </summary>
    public int DurationSeconds { get; set; } = 0;

    /// <summary>
    /// Gets or sets the message generation rate (messages per second, 0 for unlimited).
    /// </summary>
    public int MessageRateLimit { get; set; } = 0;

    /// <summary>
    /// Gets or sets the workload generator function name.
    /// </summary>
    public string? WorkloadGenerator { get; set; }
}