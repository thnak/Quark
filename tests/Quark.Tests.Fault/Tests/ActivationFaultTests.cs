using Quark.Tests.Fault.Grains;
using Xunit;

namespace Quark.Tests.Fault.Tests;

[Trait("category", "fault")]
public sealed class ActivationFaultTests : IAsyncDisposable
{
    private FaultFixture _fixture = null!;

    public ValueTask DisposeAsync() => _fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// WorkerGrain.CreateInstance throws on the 1st and 2nd activation attempts.
    /// The orchestrator catches each exception and retries. The 3rd activation succeeds,
    /// so the order completes. Requires that faulted activation entries are evicted so
    /// each retry creates a fresh activation.
    /// </summary>
    [Fact]
    public async Task Activation_WorkerCrashMidCall_OrchestratorReceivesException()
    {
        _fixture = new FaultFixture(s =>
        {
            s.Activations.ThrowOnNthActivation<WorkerGrain>(1);
            s.Activations.ThrowOnNthActivation<WorkerGrain>(2);
        });

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-activation-crash");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-crash"]);
        Assert.Equal(OrchestratorStatus.Completed, result);
    }
}
