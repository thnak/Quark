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
    public async Task MethodWithDelegateParameterAsync(Action callback)
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

/// <summary>
/// Test actor for reentrancy detection (QUARK007).
/// Non-reentrant actor calling its own methods.
/// </summary>
[Actor(Name = "ReentrancyTest", Reentrant = false)]
public class ReentrancyTestActor : ActorBase
{
    public ReentrancyTestActor(string actorId) : base(actorId)
    {
    }

    // This should trigger QUARK007 - calling another method on same actor
    public async Task OuterMethodAsync()
    {
        await this.InnerMethodAsync(); // QUARK007: Potential reentrancy
    }

    public async Task InnerMethodAsync()
    {
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test actor for performance anti-patterns (QUARK008, QUARK009).
/// </summary>
[Actor(Name = "PerformanceTest")]
public class PerformanceAntiPatternActor : ActorBase
{
    public PerformanceAntiPatternActor(string actorId) : base(actorId)
    {
    }

    // This should trigger QUARK008 - Thread.Sleep blocks
    public async Task BlockingMethodAsync()
    {
        Thread.Sleep(1000); // QUARK008: Blocking call
        await Task.CompletedTask;
    }

    // This should trigger QUARK008 - Task.Result blocks
    public async Task TaskResultBlockingAsync()
    {
        var task = Task.FromResult(42);
        var result = task.Result; // QUARK008: Blocking call
        await Task.CompletedTask;
    }

    // This should trigger QUARK009 - Synchronous file I/O
    public async Task SyncFileIoAsync(string filePath)
    {
        var content = File.ReadAllText(filePath); // QUARK009: Synchronous I/O
        await Task.CompletedTask;
    }

    // Proper async pattern - should NOT trigger warnings
    public async Task ProperAsyncPatternAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        await Task.Delay(100);
    }
}
