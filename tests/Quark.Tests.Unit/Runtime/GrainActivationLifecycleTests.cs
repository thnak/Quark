using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class GrainActivationLifecycleTests
{
    private static GrainActivation MakeActivation(GrainId id)
        => new(id, id.Type, isReentrant: false,
            new NullServiceProvider(), NullLogger<GrainActivation>.Instance);

    [Fact]
    public async Task Activation_StartsInActivatingState()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        await using var activation = MakeActivation(id);
        Assert.Equal(GrainActivationStatus.Activating, activation.ActivationStatus);
    }

    [Fact]
    public async Task MarkActive_SetsStatusToActive()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        await using var activation = MakeActivation(id);
        activation.MarkActive();
        Assert.Equal(GrainActivationStatus.Active, activation.ActivationStatus);
    }

    [Fact]
    public async Task Deactivate_TransitionsToInactive()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        await using var activation = MakeActivation(id);
        activation.MarkActive();
        activation.Deactivate(DeactivationReason.ApplicationRequested);
        await Task.Delay(200);
        Assert.Equal(GrainActivationStatus.Inactive, activation.ActivationStatus);
    }

    [Fact]
    public async Task DisposeAsync_TransitionsToInactive()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        var activation = MakeActivation(id);
        activation.MarkActive();
        await activation.DisposeAsync();
        Assert.Equal(GrainActivationStatus.Inactive, activation.ActivationStatus);
    }

    [Fact]
    public async Task PostAsync_RunsWorkItemsInOrder()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        await using var activation = MakeActivation(id);
        var results = new List<int>();
        await activation.PostAsync(() => { results.Add(1); return Task.CompletedTask; });
        await activation.PostAsync(() => { results.Add(2); return Task.CompletedTask; });
        await activation.PostAsync(() => { results.Add(3); return Task.CompletedTask; });
        Assert.Equal([1, 2, 3], results);
    }

    [Fact]
    public async Task GrainId_MatchesConstructorArgument()
    {
        var id = new GrainId(new GrainType("MyGrain"), "key-42");
        await using var activation = MakeActivation(id);
        Assert.Equal(id, activation.GrainId);
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
