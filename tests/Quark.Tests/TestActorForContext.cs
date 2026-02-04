using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

[Actor]
public class TestActorForContext : ActorBase
{
    public string? CapturedContextActorId { get; private set; }
    public string? CapturedDeactivationContextActorId { get; private set; }

    public TestActorForContext(string actorId) : base(actorId)
    {
    }

    protected override Task OnActivateWithContextAsync(CancellationToken cancellationToken = default)
    {
        // Capture the context during activation
        CapturedContextActorId = Context?.ActorId;
        return Task.CompletedTask;
    }

    protected override Task OnDeactivateWithContextAsync(CancellationToken cancellationToken = default)
    {
        // Capture the context during deactivation
        CapturedDeactivationContextActorId = Context?.ActorId;
        return Task.CompletedTask;
    }

    public Task<string?> GetContextActorIdAsync()
    {
        // Create a scope to test context availability
        var context = new ActorContext(ActorId);
        using var _ = ActorContext.CreateScope(context);
        return Task.FromResult(Context?.ActorId);
    }
}