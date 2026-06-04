using Quark.Serialization.Abstractions.Attributes;

namespace Quark.Tests.Fault.Grains;

[GenerateSerializer]
[Alias("OrchestratorState")]
public sealed record OrchestratorState
{
    [Id(0)] public string[] WorkerIds { get; init; } = [];
    [Id(1)] public int CompletionCount { get; init; }
    [Id(2)] public OrchestratorStatus Status { get; init; } = OrchestratorStatus.Pending;
}
