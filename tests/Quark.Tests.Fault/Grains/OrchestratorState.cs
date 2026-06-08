using Quark.Serialization.Abstractions.Attributes;

namespace Quark.Tests.Fault.Grains;

[GenerateSerializer]
[Alias("OrchestratorState")]
public sealed class OrchestratorState
{
    [Id(0)] public string[] WorkerIds { get; set; } = [];
    [Id(1)] public int CompletionCount { get; set; }
    [Id(2)] public OrchestratorStatus Status { get; set; } = OrchestratorStatus.Pending;
}
