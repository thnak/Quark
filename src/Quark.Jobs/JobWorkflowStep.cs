namespace Quark.Jobs;

internal sealed class JobWorkflowStep
{
    public required string StepName { get; init; }
    public required string JobId { get; init; }
    public required string JobType { get; init; }
    public required byte[] Payload { get; init; }
    public int Priority { get; init; }
    public List<string> DependsOn { get; init; } = new();
}