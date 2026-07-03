using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Tests.Unit.FailureSemantics;

public sealed class FailureBehavior : IGrainBehavior, IFailureGrain
{
    private readonly IActivationMemory<FailureState> _memory;

    public FailureBehavior(IActivationMemory<FailureState> memory)
    {
        _memory = memory;
    }

    public Task SetAsync(int value)
    {
        _memory.Value.Value = value;
        return Task.CompletedTask;
    }

    public Task<int> GetAsync() => Task.FromResult(_memory.Value.Value);

    public Task ThrowAsync(string message) => throw new InvalidOperationException(message);

    public Task SetThenThrowAsync(int value)
    {
        _memory.Value.Value = value;
        throw new InvalidOperationException($"Simulated failure after setting value to {value}.");
    }
}
