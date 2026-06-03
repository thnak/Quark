using Quark.Serialization.Abstractions.Attributes;

namespace Quark.Tests.Fault.Grains;

public enum OrchestratorStatus { Pending, Processing, Completed, Failed }

[GenerateSerializer]
[Alias("OrchestratorState")]
public sealed record OrchestratorState
{
    [Id(0)] public string[] WorkerIds { get; init; } = [];
    [Id(1)] public int CompletionCount { get; init; }
    [Id(2)] public OrchestratorStatus Status { get; init; } = OrchestratorStatus.Pending;
}
