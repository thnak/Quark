using System.Collections.Concurrent;
using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

/// <summary>
/// Test actor for verifying sequential processing.
/// </summary>
[Actor(Name = "SequentialTest")]
public class SequentialTestActor : ActorBase
{
    private int _counter = 0;
    public readonly ConcurrentBag<int> ProcessedMessages = new();
    
    public SequentialTestActor(string actorId) : base(actorId) { }
    
    public async Task<int> ProcessAsync(int value)
    {
        // Simulate some work
        await Task.Delay(10);
        
        // Increment counter (should be sequential if no concurrent access)
        var current = _counter;
        _counter = current + 1;
        
        ProcessedMessages.Add(value);
        
        return _counter;
    }
    
    public int FinalCounter => _counter;
}