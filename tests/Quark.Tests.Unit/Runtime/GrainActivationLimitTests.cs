using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Resource-exhaustion coverage for the activation cap (issue #55 [A2]):
///     <see cref="GrainActivationTable"/> must refuse to create activations for *new* grains once
///     <see cref="SiloRuntimeOptions.MaxActivations"/> is reached, so a peer cannot spawn unbounded
///     mailboxes by addressing a flood of distinct GrainIds. Existing activations stay reachable, and
///     the default (zero) means unlimited — preserving the prior behaviour.
/// </summary>
public sealed class GrainActivationLimitTests
{
    private static readonly GrainType Type = new("Capped");

    private static GrainId Id(string key) => new(Type, key);

    private static GrainActivation Probe(GrainId id) =>
        GrainActivation.CreateProbe(id, Type, new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider());

    private static GrainActivationTable Table(int maxActivations) =>
        new(NullLogger<GrainActivationTable>.Instance,
            Options.Create(new SiloRuntimeOptions { MaxActivations = maxActivations }));

    [Fact]
    public async Task GetOrCreateAsync_Rejects_New_Grain_When_MaxActivations_Reached()
    {
        GrainActivationTable table = Table(maxActivations: 2);
        await table.GetOrCreateAsync(Id("a"), () => ValueTask.FromResult(Probe(Id("a"))));
        await table.GetOrCreateAsync(Id("b"), () => ValueTask.FromResult(Probe(Id("b"))));

        await Assert.ThrowsAsync<GrainActivationLimitExceededException>(
            async () => await table.GetOrCreateAsync(Id("c"), () => ValueTask.FromResult(Probe(Id("c")))));

        Assert.Equal(2, table.Count);
    }

    [Fact]
    public async Task GetOrCreateAsync_Returns_Existing_Activation_Even_At_Capacity()
    {
        GrainActivationTable table = Table(maxActivations: 1);
        GrainActivation a = Probe(Id("a"));
        await table.GetOrCreateAsync(Id("a"), () => ValueTask.FromResult(a));

        // Re-requesting the SAME grain must not be rejected — it is already counted.
        GrainActivation again = await table.GetOrCreateAsync(Id("a"), () => ValueTask.FromResult(Probe(Id("a"))));

        Assert.Same(a, again);
    }

    [Fact]
    public async Task GetOrCreateAsync_Unlimited_By_Default()
    {
        GrainActivationTable table = Table(maxActivations: 0);
        for (int i = 0; i < 50; i++)
        {
            GrainId id = Id(i.ToString());
            await table.GetOrCreateAsync(id, () => ValueTask.FromResult(Probe(id)));
        }

        Assert.Equal(50, table.Count);
    }
}
