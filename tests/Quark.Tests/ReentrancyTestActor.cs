using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

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