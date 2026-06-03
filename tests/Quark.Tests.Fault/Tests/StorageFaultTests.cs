using Quark.Tests.Fault.Grains;
using Xunit;

namespace Quark.Tests.Fault.Tests;

[Trait("category", "fault")]
public sealed class StorageFaultTests : IAsyncDisposable
{
    private FaultFixture _fixture = null!;

    public ValueTask DisposeAsync() => _fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Storage throws on the orchestrator's first write (initial WriteStateAsync).
    /// First ProcessAsync call throws. Second call succeeds because write counter is now past N=1.
    /// </summary>
    [Fact]
    public async Task Storage_FailOnWrite_OrchestratorRetries()
    {
        _fixture = new FaultFixture(s =>
            s.OrchestratorStorage.ThrowOnNthWrite<InvalidOperationException>(1));

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-write-fail");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ProcessAsync(["w1"]));

        OrchestratorStatus result = await orchestrator.ProcessAsync(["w1"]);
        Assert.Equal(OrchestratorStatus.Completed, result);
    }

    /// <summary>
    /// Storage returns stale WorkerState (Status=Processing) on the 1st read.
    /// WorkerGrain.OnActivateAsync detects Status==Processing on load and resets to Idle.
    /// DoWorkAsync then completes normally.
    /// </summary>
    [Fact]
    public async Task Storage_FailOnRead_WorkerReactivatesClean()
    {
        var staleState = new Grains.WorkerState { Status = Grains.WorkerStatus.Processing, JobId = "stale-job" };

        _fixture = new FaultFixture(s =>
            s.WorkerStorage.ReturnStaleOnNthRead(1, staleState));

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-stale-read");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-stale"]);

        Assert.Equal(OrchestratorStatus.Completed, result);
    }
}
