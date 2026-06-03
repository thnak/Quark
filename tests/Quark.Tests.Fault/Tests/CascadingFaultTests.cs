using Quark.Core.Abstractions.Identity;
using Quark.Tests.Fault.Grains;
using Xunit;

namespace Quark.Tests.Fault.Tests;

[Trait("category", "fault")]
public sealed class CascadingFaultTests : IAsyncDisposable
{
    private FaultFixture _fixture = null!;

    public ValueTask DisposeAsync() => _fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Cascading failure: storage read throws during the 1st activation (OnActivateAsync fails
    /// because ReadStateAsync throws). The faulted activation is evicted so retries can
    /// re-activate. The activator then crashes on the 2nd and 3rd activation attempts.
    /// Orchestrator exhausts all 3 attempts → order Failed.
    /// </summary>
    [Fact]
    public async Task Cascading_StorageFail_Then_ActivationCrash()
    {
        _fixture = new FaultFixture(s =>
        {
            // 1st activation attempt: storage read throws during OnActivateAsync → activation fails
            s.WorkerStorage.ThrowOnNthRead<InvalidOperationException>(1);
            // 2nd activation attempt: activator crashes
            s.Activations.ThrowOnNthActivation<WorkerGrain>(2);
            // 3rd activation attempt: activator crashes — all retries exhausted
            s.Activations.ThrowOnNthActivation<WorkerGrain>(3);
        });

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-cascade-1");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-cascade"]);

        Assert.Equal(OrchestratorStatus.Failed, result);
    }

    /// <summary>
    /// 1st attempt: call to w-recovery is dropped (ThrowOnNthCallToType fires on call #1).
    /// 2nd attempt (retry): worker activates, reads stale state (Status=Processing),
    ///   OnActivateAsync resets to Idle, but DoWorkAsync storage write fails.
    /// 3rd attempt (retry): same activated worker, write succeeds → DoWorkAsync completes.
    /// Expected: order Completed.
    /// </summary>
    [Fact]
    public async Task Cascading_TransportDrop_Then_StorageFail_Then_Reactivation()
    {
        var staleState = new WorkerState { Status = WorkerStatus.Processing };

        _fixture = new FaultFixture(s =>
        {
            // 1st call attempt: dropped before the grain is activated
            s.Calls.ThrowOnNthCallToType(
                new GrainType("WorkerGrain"),
                1,
                () => new InvalidOperationException("Simulated call drop"));

            // 2nd attempt: worker activates and reads stale state (Status=Processing),
            // OnActivateAsync resets to Idle, then DoWorkAsync write fails
            s.WorkerStorage.ReturnStaleOnNthRead(1, staleState);
            s.WorkerStorage.ThrowOnNthWrite<InvalidOperationException>(1);
        });

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-cascade-2");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-recovery"]);

        Assert.Equal(OrchestratorStatus.Completed, result);
    }
}
