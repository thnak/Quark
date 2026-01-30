using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.ProtoProxy;

/// <summary>
/// Example actor for demonstrating proto proxy generation.
/// </summary>
[Actor(Name = "Calculator")]
public class CalculatorActor : ActorBase
{
    public CalculatorActor(string actorId) : base(actorId)
    {
    }

    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(10); // Simulate async work
        return a + b;
    }

    public async Task<int> MultiplyAsync(int a, int b)
    {
        await Task.Delay(10); // Simulate async work
        return a * b;
    }

    public async Task<string> GetStatusAsync()
    {
        await Task.CompletedTask;
        return $"Calculator {ActorId} is active";
    }
}
