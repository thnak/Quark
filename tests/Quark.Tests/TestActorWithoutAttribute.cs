using Quark.Core.Actors;

namespace Quark.Tests;

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