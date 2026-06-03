using Quark.Core.Abstractions.Identity;
using Quark.Tests.Fault.Grains;
using Xunit;

namespace Quark.Tests.Fault.Tests;

[Trait("category", "fault")]
public sealed class TransportFaultTests : IAsyncDisposable
{
    private FaultFixture _fixture = null!;

    public ValueTask DisposeAsync() => _fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// All calls to worker "w1" are dropped (AlwaysThrowForKey).
    /// Workers "w2" and "w3" succeed. Orchestrator retries w1 3 times, marks order Failed.
    /// Second ProcessAsync with only w2 and w3 completes.
    /// </summary>
    [Fact]
    public async Task Transport_DropMidFanout_OrchestratorHandlesPartialResults()
    {
        _fixture = new FaultFixture(s =>
            s.Calls.AlwaysThrowForKey(
                new GrainType("WorkerGrain"),
                "w1",
                () => new InvalidOperationException("Simulated call drop for w1")));

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-drop");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w1", "w2", "w3"]);

        Assert.Equal(OrchestratorStatus.Failed, result);

        OrchestratorStatus result2 = await orchestrator.ProcessAsync(["w2", "w3"]);
        Assert.Equal(OrchestratorStatus.Completed, result2);
    }
}
