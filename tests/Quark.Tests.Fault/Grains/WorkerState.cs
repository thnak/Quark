using Quark.Serialization.Abstractions.Attributes;

namespace Quark.Tests.Fault.Grains;

[GenerateSerializer]
[Alias("WorkerState")]
public sealed class WorkerState
{
    [Id(0)] public string JobId { get; set; } = "";
    [Id(1)] public WorkerStatus Status { get; set; } = WorkerStatus.Idle;
    [Id(2)] public int RetryCount { get; set; }
    [Id(3)] public DateTimeOffset? ProcessedAt { get; set; }
}
