using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.ContextRegistration;

/// <summary>
/// Server-side implementation of the calculator service.
/// This implements the external interface and can be hosted as an actor.
/// </summary>
[Actor(Name = "Calculator")]
public class CalculatorActor : ActorBase, ICalculatorService
{
    private readonly List<string> _history = new();
    
    public CalculatorActor(string actorId) : base(actorId)
    {
    }

    public Task<int> AddAsync(int a, int b)
    {
        var result = a + b;
        _history.Add($"Add: {a} + {b} = {result}");
        return Task.FromResult(result);
    }

    public Task<int> MultiplyAsync(int x, int y)
    {
        var result = x * y;
        _history.Add($"Multiply: {x} * {y} = {result}");
        return Task.FromResult(result);
    }

    public Task<string> GetHistoryAsync()
    {
        return Task.FromResult(string.Join("\n", _history));
    }
}
