using Quark.Serialization.Abstractions.Attributes;

namespace Quark.Tests.Fault.Grains;

[GenerateSerializer]
[Alias("WorkerState")]
public sealed record WorkerState
{
    [Id(0)] public string JobId { get; init; } = "";
    [Id(1)] public WorkerStatus Status { get; init; } = WorkerStatus.Idle;
    [Id(2)] public int RetryCount { get; init; }
    [Id(3)] public DateTimeOffset? ProcessedAt { get; init; }
}
