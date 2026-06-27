using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Deterministic, clock-injected unit coverage for idle-collection logic (issue #35):
///     the <see cref="GrainActivation.IsIdleLongerThan"/> / <see cref="GrainActivation"/>
///     deactivation-allowed predicates that drive <see cref="GrainIdleCollector"/>, the
///     collector's disabled-by-default early return, and <see cref="SiloRuntimeOptions"/> defaults.
///     Replaces reliance on real-time sleeps used by the integration tests.
/// </summary>
public sealed class GrainIdleCollectionTests
{
    private static GrainActivation MakeActivation()
    {
        var id = new GrainId(new GrainType("IdleTest"), "1");
        return new GrainActivation(id, id.Type, isReentrant: false,
            new NullServiceProvider(), NullLogger<GrainActivation>.Instance);
    }

    // -------------------------------------------------------------------------
    // IsIdleLongerThan — strict boundary semantics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsIdleLongerThan_FreshActivation_IsNotIdle()
    {
        await using GrainActivation activation = MakeActivation();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.False(activation.IsIdleLongerThan(TimeSpan.FromMinutes(5), now));
    }

    [Fact]
    public async Task IsIdleLongerThan_AtTheAge_IsNotCollected()
    {
        // last-accessed is stamped during construction; bracket it with before/after so the
        // assertion holds regardless of the sub-millisecond construction skew.
        DateTimeOffset before = DateTimeOffset.UtcNow;
        await using GrainActivation activation = MakeActivation();
        TimeSpan age = TimeSpan.FromMinutes(5);

        // now - lastAccessed <= age  (because lastAccessed >= before)  →  not strictly past  →  not idle
        Assert.False(activation.IsIdleLongerThan(age, before + age));
    }

    [Fact]
    public async Task IsIdleLongerThan_StrictlyPastTheAge_IsCollected()
    {
        await using GrainActivation activation = MakeActivation();
        DateTimeOffset after = DateTimeOffset.UtcNow;
        TimeSpan age = TimeSpan.FromMinutes(5);

        // now - lastAccessed > age  (because lastAccessed <= after)  →  idle
        Assert.True(activation.IsIdleLongerThan(age, after + age + TimeSpan.FromMilliseconds(1)));
    }

    // -------------------------------------------------------------------------
    // IsDeactivationAllowed + DelayDeactivation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsDeactivationAllowed_ByDefault_IsTrue()
    {
        await using GrainActivation activation = MakeActivation();

        Assert.True(activation.IsDeactivationAllowed(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task DelayDeactivation_BlocksDeactivation_WithinTheDelayWindow()
    {
        await using GrainActivation activation = MakeActivation();
        DateTimeOffset callTime = DateTimeOffset.UtcNow;
        activation.DelayDeactivation(TimeSpan.FromMinutes(10));

        // deadline >= callTime + 10min, so a moment 5min after the call is still inside the window.
        Assert.False(activation.IsDeactivationAllowed(callTime + TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task DelayDeactivation_AllowsDeactivation_OnceTheDeadlinePasses()
    {
        await using GrainActivation activation = MakeActivation();
        DateTimeOffset callTime = DateTimeOffset.UtcNow;
        activation.DelayDeactivation(TimeSpan.FromMinutes(10));

        // deadline <= callTime + 10min + ε, so a moment 20min after the call is comfortably past it.
        Assert.True(activation.IsDeactivationAllowed(callTime + TimeSpan.FromMinutes(20)));
    }

    [Fact]
    public async Task DelayedActivation_IdlePastTheAge_IsNotCollectable()
    {
        // Mirrors the collector's decision: collect only when idle past the age AND deactivation
        // is allowed. A delayed grain that is idle past the age must still be skipped.
        await using GrainActivation activation = MakeActivation();
        DateTimeOffset callTime = DateTimeOffset.UtcNow;
        activation.DelayDeactivation(TimeSpan.FromMinutes(10));

        TimeSpan age = TimeSpan.FromMinutes(1);
        DateTimeOffset now = callTime + TimeSpan.FromMinutes(5);

        Assert.True(activation.IsIdleLongerThan(age, now));         // idle past the age …
        Assert.False(activation.IsDeactivationAllowed(now));        // … but the delay still blocks it
        Assert.False(activation.IsIdleLongerThan(age, now) && activation.IsDeactivationAllowed(now));
    }

    // -------------------------------------------------------------------------
    // GrainIdleCollector — disabled-by-default early return
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IdleCollector_WhenCollectionAgeIsZero_ReturnsImmediately()
    {
        var table = new GrainActivationTable(NullLogger<GrainActivationTable>.Instance);
        var options = Options.Create(new SiloRuntimeOptions { GrainCollectionAge = TimeSpan.Zero });
        var collector = new GrainIdleCollector(table, options);

        await collector.StartAsync(CancellationToken.None);
        try
        {
            // With collection disabled, ExecuteAsync returns immediately — the background task
            // completes promptly instead of parking on a PeriodicTimer loop.
            Task? execute = collector.ExecuteTask;
            if (execute is not null)
                await execute.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(execute is null || execute.IsCompletedSuccessfully,
                $"Expected the disabled collector to finish; status={execute?.Status.ToString() ?? "null"}");
        }
        finally
        {
            await collector.StopAsync(CancellationToken.None);
            collector.Dispose();
        }
    }

    [Fact]
    public async Task IdleCollector_WhenCollectionAgeIsPositive_KeepsRunning()
    {
        var table = new GrainActivationTable(NullLogger<GrainActivationTable>.Instance);
        var options = Options.Create(new SiloRuntimeOptions
        {
            GrainCollectionAge = TimeSpan.FromMinutes(5),
            GrainCollectionInterval = TimeSpan.FromMinutes(1),
        });
        var collector = new GrainIdleCollector(table, options);

        await collector.StartAsync(CancellationToken.None);
        try
        {
            // Enabled: the collector parks on its PeriodicTimer rather than completing.
            Assert.NotNull(collector.ExecuteTask);
            Assert.False(collector.ExecuteTask!.IsCompleted);
        }
        finally
        {
            await collector.StopAsync(CancellationToken.None);
            collector.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // SiloRuntimeOptions defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void SiloRuntimeOptions_Defaults_DisableCollectionWithOneMinuteInterval()
    {
        var options = new SiloRuntimeOptions();

        Assert.Equal(TimeSpan.Zero, options.GrainCollectionAge);
        Assert.Equal(TimeSpan.FromMinutes(1), options.GrainCollectionInterval);
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
