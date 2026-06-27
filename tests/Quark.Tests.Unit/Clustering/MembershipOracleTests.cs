using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Runtime.Clustering;
using Xunit;

namespace Quark.Tests.Unit.Clustering;

/// <summary>
///     Deterministic failure-mode coverage for <see cref="MembershipOracle"/> dead-silo
///     detection (issue #33). Drives <see cref="MembershipOracle.EvictDeadSilosAsync"/> and
///     <see cref="MembershipOracle.MarkSelfDeadAsync"/> directly against an
///     <see cref="InMemoryMembershipTable"/> with timestamps seeded relative to
///     <see cref="MembershipOracle.DeadSiloThreshold"/>, so detection fires without 30s waits.
/// </summary>
public sealed class MembershipOracleTests
{
    private static readonly SiloAddress Self = SiloAddress.Loopback(11111);
    private static readonly SiloAddress Remote = SiloAddress.Loopback(11112);
    private static readonly SiloAddress Survivor = SiloAddress.Loopback(11113);

    private static DateTime Stale =>
        DateTime.UtcNow - MembershipOracle.DeadSiloThreshold - TimeSpan.FromSeconds(1);

    private static DateTime Fresh => DateTime.UtcNow - TimeSpan.FromSeconds(1);

    private static MembershipOracle CreateOracle(
        IMembershipTable table, ISiloRouter router, SiloAddress self)
    {
        var options = Options.Create(new SiloRuntimeOptions { SiloAddress = self, SiloName = "self" });
        return new MembershipOracle(
            table, router, new NoOpGrainDirectory(), options, NullLogger<MembershipOracle>.Instance);
    }

    private static MembershipEntry Entry(SiloAddress address, SiloStatus status, DateTime iAmAlive) =>
        new() { SiloAddress = address, SiloName = address.ToString(), Status = status, IAmAlive = iAmAlive };

    private static async Task<SiloStatus> StatusOf(IMembershipTable table, SiloAddress address)
    {
        IReadOnlyList<MembershipEntry> all = await table.ReadAllAsync();
        return all.Single(e => e.SiloAddress == address).Status;
    }

    // =====================================================================
    // EvictDeadSilosAsync
    // =====================================================================

    [Fact]
    public async Task EvictDeadSilos_StaleRemote_IsMarkedDead_AndUnregisteredOnce()
    {
        var table = new InMemoryMembershipTable();
        var router = new RecordingSiloRouter();
        await table.InsertRowAsync(Entry(Self, SiloStatus.Active, DateTime.UtcNow));
        await table.InsertRowAsync(Entry(Remote, SiloStatus.Active, Stale));

        MembershipOracle oracle = CreateOracle(table, router, Self);
        await oracle.EvictDeadSilosAsync(CancellationToken.None);

        Assert.Equal(SiloStatus.Dead, await StatusOf(table, Remote));
        Assert.Equal(new[] { Remote }, router.Unregistered);
    }

    [Fact]
    public async Task EvictDeadSilos_FreshRemote_IsLeftActive_AndNotUnregistered()
    {
        var table = new InMemoryMembershipTable();
        var router = new RecordingSiloRouter();
        await table.InsertRowAsync(Entry(Self, SiloStatus.Active, DateTime.UtcNow));
        await table.InsertRowAsync(Entry(Remote, SiloStatus.Active, Fresh));

        MembershipOracle oracle = CreateOracle(table, router, Self);
        await oracle.EvictDeadSilosAsync(CancellationToken.None);

        Assert.Equal(SiloStatus.Active, await StatusOf(table, Remote));
        Assert.Empty(router.Unregistered);
    }

    [Fact]
    public async Task EvictDeadSilos_AlreadyDeadRemote_IsSkipped_NoSpuriousUnregister()
    {
        var table = new InMemoryMembershipTable();
        var router = new RecordingSiloRouter();
        await table.InsertRowAsync(Entry(Self, SiloStatus.Active, DateTime.UtcNow));
        // Already Dead even though its heartbeat is stale — must not be re-evicted.
        await table.InsertRowAsync(Entry(Remote, SiloStatus.Dead, Stale));

        MembershipOracle oracle = CreateOracle(table, router, Self);
        await oracle.EvictDeadSilosAsync(CancellationToken.None);

        Assert.Empty(router.Unregistered);
    }

    [Fact]
    public async Task EvictDeadSilos_SelfEntry_IsNeverEvicted_EvenWhenStale()
    {
        var table = new InMemoryMembershipTable();
        var router = new RecordingSiloRouter();
        // Self heartbeat is stale, but the oracle must never evict its own row.
        await table.InsertRowAsync(Entry(Self, SiloStatus.Active, Stale));

        MembershipOracle oracle = CreateOracle(table, router, Self);
        await oracle.EvictDeadSilosAsync(CancellationToken.None);

        Assert.Equal(SiloStatus.Active, await StatusOf(table, Self));
        Assert.Empty(router.Unregistered);
    }

    [Fact]
    public async Task EvictDeadSilos_OnlyStaleSilosAreEvicted_FreshAndDeadUntouched()
    {
        var table = new InMemoryMembershipTable();
        var router = new RecordingSiloRouter();
        var stale = SiloAddress.Loopback(12001);
        var fresh = SiloAddress.Loopback(12002);
        var dead = SiloAddress.Loopback(12003);
        await table.InsertRowAsync(Entry(Self, SiloStatus.Active, DateTime.UtcNow));
        await table.InsertRowAsync(Entry(stale, SiloStatus.Active, Stale));
        await table.InsertRowAsync(Entry(fresh, SiloStatus.Active, Fresh));
        await table.InsertRowAsync(Entry(dead, SiloStatus.Dead, Stale));

        MembershipOracle oracle = CreateOracle(table, router, Self);
        await oracle.EvictDeadSilosAsync(CancellationToken.None);

        Assert.Equal(new[] { stale }, router.Unregistered);
        Assert.Equal(SiloStatus.Dead, await StatusOf(table, stale));
        Assert.Equal(SiloStatus.Active, await StatusOf(table, fresh));
    }

    [Fact]
    public async Task EvictDeadSilos_RemovesDeadSiloFromRouter_LeavingSurvivorReachable()
    {
        var table = new InMemoryMembershipTable();
        var router = new RecordingSiloRouter();
        router.Register(Remote, new StubInvoker());
        router.Register(Survivor, new StubInvoker());
        await table.InsertRowAsync(Entry(Self, SiloStatus.Active, DateTime.UtcNow));
        await table.InsertRowAsync(Entry(Remote, SiloStatus.Active, Stale));
        await table.InsertRowAsync(Entry(Survivor, SiloStatus.Active, Fresh));

        MembershipOracle oracle = CreateOracle(table, router, Self);
        await oracle.EvictDeadSilosAsync(CancellationToken.None);

        Assert.False(router.TryGetInvoker(Remote, out _));      // dead silo no longer routable
        Assert.True(router.TryGetInvoker(Survivor, out _));     // survivor still reachable
        // A silo can re-register at the evicted address (e.g. after restart).
        router.Register(Remote, new StubInvoker());
        Assert.True(router.TryGetInvoker(Remote, out _));
    }

    // =====================================================================
    // MarkSelfDeadAsync
    // =====================================================================

    [Fact]
    public async Task MarkSelfDead_WritesSelfStatusDead()
    {
        var table = new InMemoryMembershipTable();
        await table.InsertRowAsync(Entry(Self, SiloStatus.Active, DateTime.UtcNow));

        MembershipOracle oracle = CreateOracle(table, new RecordingSiloRouter(), Self);
        await oracle.MarkSelfDeadAsync();

        Assert.Equal(SiloStatus.Dead, await StatusOf(table, Self));
    }

    [Fact]
    public async Task MarkSelfDead_SwallowsTableWriteFailure()
    {
        // A failed shutdown write must not escape ExecuteAsync's final await.
        MembershipOracle oracle = CreateOracle(new ThrowingOnUpdateTable(), new RecordingSiloRouter(), Self);

        // Must not throw.
        await oracle.MarkSelfDeadAsync();
    }

    // =====================================================================
    // Fixtures
    // =====================================================================

    private sealed class RecordingSiloRouter : ISiloRouter
    {
        private readonly Dictionary<SiloAddress, IGrainCallInvoker> _invokers = new();
        public List<SiloAddress> Unregistered { get; } = [];

        public void Register(SiloAddress address, IGrainCallInvoker invoker) => _invokers[address] = invoker;

        public void Unregister(SiloAddress address)
        {
            Unregistered.Add(address);
            _invokers.Remove(address);
        }

        public bool TryGetInvoker(SiloAddress address, [NotNullWhen(true)] out IGrainCallInvoker? invoker)
            => _invokers.TryGetValue(address, out invoker);
    }

    private sealed class NoOpGrainDirectory : IGrainDirectory
    {
        public bool TryRegister(GrainId grainId, SiloAddress siloAddress, out SiloAddress existing)
        {
            existing = default;
            return true;
        }

        public bool TryUnregister(GrainId grainId, SiloAddress siloAddress) => true;

        public bool TryLookup(GrainId grainId, out SiloAddress siloAddress)
        {
            siloAddress = default;
            return false;
        }
    }

    private sealed class ThrowingOnUpdateTable : IMembershipTable
    {
        public Task<IReadOnlyList<MembershipEntry>> ReadAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MembershipEntry>>([]);

        public Task InsertRowAsync(MembershipEntry entry, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateRowAsync(MembershipEntry entry, CancellationToken ct = default)
            => throw new InvalidOperationException("table unavailable");

        public Task UpdateIAmAliveAsync(SiloAddress address, DateTime iAmAlive, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubInvoker : IGrainCallInvoker
    {
        public Task<TResult> InvokeAsync<TInvokable, TResult>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IGrainInvokable<TResult> => throw new NotSupportedException();

        public Task InvokeVoidAsync<TInvokable>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IGrainVoidInvokable => throw new NotSupportedException();

        public Task InvokeObserverAsync<TInvokable>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IObserverVoidInvokable => throw new NotSupportedException();
    }
}
