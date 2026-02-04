using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

/// <summary>
/// Test actor with proper attributes - should not trigger any warnings.
/// </summary>
[Actor(Name = "TestProperActor")]
public class TestProperActor : ActorBase
{
    public TestProperActor(string actorId) : base(actorId)
    {
    }

    // Proper async method
    public async Task ProperAsyncMethod(string message, int count)
    {
        await Task.CompletedTask;
    }

    // Proper method with serializable parameters
    public async Task<string> ProcessDataAsync(List<string> items, Dictionary<string, int> counts)
    {
        await Task.CompletedTask;
        return "processed";
    }
}