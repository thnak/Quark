using Quark.Tests.Fault.FaultScenario;

namespace Quark.Tests.Fault;

/// <summary>
/// Aggregates all fault plans for a single test scenario.
/// Pass an Action&lt;FaultScenario&gt; to FaultFixture to configure faults before test execution.
/// </summary>
public sealed class FaultScenarioHolder
{
    public StorageFaultPlan WorkerStorage { get; } = new();
    public StorageFaultPlan OrchestratorStorage { get; } = new();
    public CallFaultPlan Calls { get; } = new();
    public ActivationFaultPlan Activations { get; } = new();
}
