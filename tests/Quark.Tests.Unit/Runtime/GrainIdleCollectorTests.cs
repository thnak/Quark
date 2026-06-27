using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Boundary coverage for idle grain collection (issue #35): the
///     <see cref="GrainActivation.IsIdleLongerThan"/> / <see cref="GrainActivation.IsDeactivationAllowed"/>
///     predicates with an injected <c>now</c>, the <see cref="GrainIdleCollector"/> selection loop driven
///     against a seeded <see cref="GrainActivationTable"/>, the disabled-by-default
///     (<c>GrainCollectionAge == Zero</c>) path, and <see cref="SiloRuntimeOptions"/> defaults — all
///     without the real-time sleeps the integration tests use. Probe activations have no processing
///     loop and start with <c>_lastAccessedTicks == 0</c>, so idle age is controlled precisely.
/// </summary>
public sealed class GrainIdleCollectorTests
{
    private static readonly IServiceProvider Sp = new ServiceCollection().BuildServiceProvider();
    private static readonly GrainType Type = new("Idle");

    private static GrainId Id(string key) => new(Type, key);

    private static GrainActivation Probe(GrainId id) => GrainActivation.CreateProbe(id, Type, Sp);

    private static void SetLastAccessed(GrainActivation activation, long utcTicks)
        => typeof(GrainActivation)
            .GetField("_lastAccessedTicks", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(activation, utcTicks);

    private static async Task<GrainActivationTable> SeedAsync(params (GrainId id, GrainActivation act)[] entries)
    {
        var table = new GrainActivationTable(NullLogger<GrainActivationTable>.Instance);
        foreach ((GrainId id, GrainActivation act) in entries)
            await table.GetOrCreateAsync(id, () => Task.FromResult(act));
        return table;
    }

    private static GrainIdleCollector Collector(GrainActivationTable table, TimeSpan age) =>
        new(table, Options.Create(new SiloRuntimeOptions { GrainCollectionAge = age }));

    // =====================================================================
    // Predicate boundaries
    // =====================================================================

    [Fact]
    public void IsIdleLongerThan_ExactlyAtThreshold_IsNotIdle()
    {
        GrainActivation a = Probe(Id("a")); // _lastAccessedTicks == 0
        TimeSpan threshold = TimeSpan.FromMinutes(5);
        var now = new DateTimeOffset(threshold.Ticks, TimeSpan.Zero); // now - 0 == threshold

        Assert.False(a.IsIdleLongerThan(threshold, now)); // strictly greater required
    }

    [Fact]
    public void IsIdleLongerThan_JustPastThreshold_IsIdle()
    {
        GrainActivation a = Probe(Id("a"));
        TimeSpan threshold = TimeSpan.FromMinutes(5);
        var now = new DateTimeOffset(threshold.Ticks + 1, TimeSpan.Zero);

        Assert.True(a.IsIdleLongerThan(threshold, now));
    }

    [Fact]
    public void IsDeactivationAllowed_NoDelaySet_IsAlwaysAllowed()
    {
        GrainActivation a = Probe(Id("a"));

        Assert.True(a.IsDeactivationAllowed(DateTimeOffset.UtcNow));
        Assert.True(a.IsDeactivationAllowed(new DateTimeOffset(1, TimeSpan.Zero)));
    }

    [Fact]
    public void IsDeactivationAllowed_AfterDelayDeactivation_BlocksUntilDeadlinePasses()
    {
        GrainActivation a = Probe(Id("a"));
        a.DelayDeactivation(TimeSpan.FromHours(1));

        Assert.False(a.IsDeactivationAllowed(DateTimeOffset.UtcNow));
        Assert.True(a.IsDeactivationAllowed(DateTimeOffset.UtcNow.AddHours(2)));
    }

    // =====================================================================
    // Collector selection
    // =====================================================================

    [Fact]
    public async Task CollectIdleGrains_DeactivatesOnlyActivationsIdlePastTheAge()
    {
        GrainId staleId = Id("stale");
        GrainId freshId = Id("fresh");
        GrainActivation stale = Probe(staleId); // _lastAccessedTicks == 0 → very idle
        GrainActivation fresh = Probe(freshId);
        SetLastAccessed(fresh, DateTimeOffset.UtcNow.UtcTicks); // accessed just now

        GrainActivationTable table = await SeedAsync((staleId, stale), (freshId, fresh));
        Collector(table, TimeSpan.FromHours(1)).CollectIdleGrains();

        Assert.Equal(GrainActivationStatus.Deactivating, stale.ActivationStatus);
        Assert.Equal(GrainActivationStatus.Active, fresh.ActivationStatus);
    }

    [Fact]
    public async Task CollectIdleGrains_DelayDeactivation_BlocksCollection_EvenWhenIdlePastAge()
    {
        GrainId id = Id("delayed");
        GrainActivation delayed = Probe(id); // idle (lastAccessed == 0)
        delayed.DelayDeactivation(TimeSpan.FromHours(1)); // but deactivation deferred

        GrainActivationTable table = await SeedAsync((id, delayed));
        Collector(table, TimeSpan.FromHours(1)).CollectIdleGrains();

        Assert.Equal(GrainActivationStatus.Active, delayed.ActivationStatus);
    }

    [Fact]
    public async Task CollectIdleGrains_NoIdleActivations_DeactivatesNothing()
    {
        GrainId id = Id("fresh");
        GrainActivation fresh = Probe(id);
        SetLastAccessed(fresh, DateTimeOffset.UtcNow.UtcTicks);

        GrainActivationTable table = await SeedAsync((id, fresh));
        Collector(table, TimeSpan.FromHours(1)).CollectIdleGrains();

        Assert.Equal(GrainActivationStatus.Active, fresh.ActivationStatus);
    }

    // =====================================================================
    // Disabled-by-default path
    // =====================================================================

    [Fact]
    public async Task ExecuteAsync_WhenAgeIsZero_ReturnsImmediately_WithoutCollecting()
    {
        GrainId id = Id("stale");
        GrainActivation stale = Probe(id); // idle (lastAccessed == 0)
        GrainActivationTable table = await SeedAsync((id, stale));

        GrainIdleCollector collector = Collector(table, TimeSpan.Zero); // disabled (default)
        await collector.StartAsync(CancellationToken.None);

        // Disabled path returns immediately; an enabled collector would block on the
        // 1-minute PeriodicTimer and never complete within this window.
        await collector.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(collector.ExecuteTask!.IsCompletedSuccessfully); // loop never started
        Assert.Equal(GrainActivationStatus.Active, stale.ActivationStatus); // nothing collected

        await collector.StopAsync(CancellationToken.None);
    }

    // =====================================================================
    // Option defaults
    // =====================================================================

    [Fact]
    public void SiloRuntimeOptions_Defaults_DisableCollection_WithOneMinuteInterval()
    {
        var options = new SiloRuntimeOptions();

        Assert.Equal(TimeSpan.Zero, options.GrainCollectionAge);
        Assert.Equal(TimeSpan.FromMinutes(1), options.GrainCollectionInterval);
    }
}
