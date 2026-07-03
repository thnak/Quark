using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.FailureSemantics;

public sealed class FlakyActivationBehavior : IGrainBehavior, IFlakyActivationGrain
{
    public FlakyActivationBehavior(ActivationGate gate)
    {
        gate.RecordAttempt();
        if (gate.ShouldFail)
        {
            throw new InvalidOperationException("Simulated activation failure.");
        }
    }

    public Task<int> PingAsync() => Task.FromResult(1);
}
