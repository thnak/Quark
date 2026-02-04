using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

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