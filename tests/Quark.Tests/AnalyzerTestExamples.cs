using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests.AnalyzerTests;

/// <summary>
/// Test actor to verify analyzer behavior.
/// This class should trigger QUARK005 (missing [Actor] attribute).
/// </summary>
public class TestActorWithoutAttribute : ActorBase
{
    public TestActorWithoutAttribute(string actorId) : base(actorId)
    {
    }

    // This should trigger QUARK004 (sync method in actor)
    public void SynchronousMethod()
    {
        // Synchronous method
    }

    // This should NOT trigger warnings
    public async Task AsyncMethod()
    {
        await Task.CompletedTask;
    }

    // This should trigger QUARK006 (non-serializable parameter)
    public async Task MethodWithDelegateParameter(Action callback)
    {
        await Task.CompletedTask;
        callback?.Invoke();
    }
}

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
