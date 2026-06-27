using System.Collections;
using System.Reflection;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Transactions;
using Xunit;

namespace Quark.Tests.Unit.Transactions;

/// <summary>
///     Direct, deterministic unit coverage for the 2PC-style commit/rollback logic in
///     <see cref="TransactionCoordinator"/> and <see cref="TransactionalState{TState}"/> (issue #30).
///     Writers are fakes; <see cref="TransactionalState{TState}"/> is driven against an in-memory
///     <see cref="IGrainStorage"/> fake. No TestCluster or Testcontainers — pure logic.
/// </summary>
public sealed class TransactionCoordinatorTests
{
    // =====================================================================
    // TransactionCoordinator — lifecycle & AsyncLocal flow
    // =====================================================================

    [Fact]
    public void BeginTransaction_ReturnsUniqueIds()
    {
        var coordinator = new TransactionCoordinator();
        Guid first = coordinator.BeginTransaction();
        Guid second = coordinator.BeginTransaction();

        Assert.NotEqual(Guid.Empty, first);
        Assert.NotEqual(Guid.Empty, second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BeginTransaction_SetsIsInTransaction_WithReturnedId()
    {
        var coordinator = new TransactionCoordinator();
        Guid id = coordinator.BeginTransaction();

        Assert.True(coordinator.IsInTransaction(out Guid current));
        Assert.Equal(id, current);
    }

    [Fact]
    public void IsInTransaction_FreshCoordinator_ReturnsFalseAndEmpty()
    {
        var coordinator = new TransactionCoordinator();

        Assert.False(coordinator.IsInTransaction(out Guid current));
        Assert.Equal(Guid.Empty, current);
    }

    [Fact]
    public async Task CommitClear_IsScopedToTheAsyncCall_AndNotObservableByTheCaller()
    {
        // The ambient id set by BeginTransaction (synchronous → caller's own ExecutionContext)
        // flows DOWNWARD to callees. CommitAsync clears it, but that assignment happens inside an
        // async method whose ExecutionContext is saved/restored around the synchronous body
        // (AsyncMethodBuilderCore.Start), so the clear is NOT visible to the synchronous caller.
        // The observable "transaction ended" signal is dictionary removal (a second commit no-ops),
        // covered by CommitAsync_AfterCommit_IsNoOp_WritersDoNotRunTwice.
        var coordinator = new TransactionCoordinator();
        Guid id = coordinator.BeginTransaction();

        await coordinator.CommitAsync(id);

        Assert.True(coordinator.IsInTransaction(out Guid current)); // documents EC-scoping, not a contract to rely on
        Assert.Equal(id, current);
    }

    // =====================================================================
    // TransactionCoordinator — writer dispatch
    // =====================================================================

    [Fact]
    public async Task CommitAsync_InvokesEveryWriterCommit_InRegistrationOrder_AndNoRollbacks()
    {
        var coordinator = new TransactionCoordinator();
        Guid id = coordinator.BeginTransaction();

        var commits = new List<int>();
        var rollbacks = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            coordinator.RegisterWriter(id,
                () => { commits.Add(index); return Task.CompletedTask; },
                () => rollbacks.Add(index));
        }

        await coordinator.CommitAsync(id);

        Assert.Equal(new[] { 0, 1, 2 }, commits);
        Assert.Empty(rollbacks);
    }

    [Fact]
    public async Task AbortAsync_InvokesEveryWriterRollback_InRegistrationOrder_AndNoCommits()
    {
        var coordinator = new TransactionCoordinator();
        Guid id = coordinator.BeginTransaction();

        var commits = new List<int>();
        var rollbacks = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            coordinator.RegisterWriter(id,
                () => { commits.Add(index); return Task.CompletedTask; },
                () => rollbacks.Add(index));
        }

        await coordinator.AbortAsync(id, new InvalidOperationException("ignored"));

        Assert.Equal(new[] { 0, 1, 2 }, rollbacks);
        Assert.Empty(commits);
    }

    [Fact]
    public async Task CommitAsync_UnknownTransactionId_IsNoOp()
    {
        var coordinator = new TransactionCoordinator();

        // No throw, nothing to run.
        await coordinator.CommitAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task AbortAsync_UnknownTransactionId_IsNoOp()
    {
        var coordinator = new TransactionCoordinator();

        await coordinator.AbortAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task CommitAsync_AfterCommit_IsNoOp_WritersDoNotRunTwice()
    {
        var coordinator = new TransactionCoordinator();
        Guid id = coordinator.BeginTransaction();

        int commitCount = 0;
        coordinator.RegisterWriter(id, () => { commitCount++; return Task.CompletedTask; }, () => { });

        await coordinator.CommitAsync(id);
        await coordinator.CommitAsync(id); // already removed → no-op

        Assert.Equal(1, commitCount);
    }

    [Fact]
    public async Task RegisterWriter_UnknownTransactionId_IsSilentlyIgnored()
    {
        var coordinator = new TransactionCoordinator();
        Guid known = coordinator.BeginTransaction();

        bool bogusRan = false;
        // Register against an id that was never begun — must not throw …
        coordinator.RegisterWriter(Guid.NewGuid(), () => { bogusRan = true; return Task.CompletedTask; }, () => { });

        // … and must not run when an unrelated transaction commits.
        await coordinator.CommitAsync(known);

        Assert.False(bogusRan);
    }

    [Fact]
    public async Task CommitAsync_WriterThrows_PriorWritersAlreadyCommitted_AndAreNotCompensated()
    {
        // Documents the known partial-commit gap: writers commit in sequence and a later
        // failure does NOT roll back earlier-committed writers (issue #30).
        var coordinator = new TransactionCoordinator();
        Guid id = coordinator.BeginTransaction();

        bool firstCommitted = false;
        bool firstRolledBack = false;

        coordinator.RegisterWriter(id,
            () => { firstCommitted = true; return Task.CompletedTask; },
            () => firstRolledBack = true);
        coordinator.RegisterWriter(id,
            () => throw new InvalidOperationException("commit boom"),
            () => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CommitAsync(id));

        Assert.True(firstCommitted);       // first writer committed before the failure …
        Assert.False(firstRolledBack);     // … and was NOT compensated (partial commit stands)
    }

    // =====================================================================
    // TransactionalState — pending/committed semantics & writer dedup
    // =====================================================================

    [Fact]
    public async Task PerformRead_BeforeAnyUpdate_ReturnsCommittedState()
    {
        var storage = new FakeGrainStorage();
        var coordinator = new TransactionCoordinator();
        TransactionalState<Counter> state = MakeState(storage, coordinator);

        int value = await state.PerformRead(s => s.Value);

        Assert.Equal(0, value);              // freshly loaded committed default
        Assert.Equal(1, storage.ReadCount);  // lazy load happened once
    }

    [Fact]
    public async Task PerformUpdate_OutsideTransaction_MutatesPending_WithoutRegisteringAWriter()
    {
        var storage = new FakeGrainStorage();
        var coordinator = new TransactionCoordinator();
        TransactionalState<Counter> state = MakeState(storage, coordinator);

        await state.PerformUpdate(s => s.Value = 42);

        // PerformRead now sees the pending value …
        Assert.Equal(42, await state.PerformRead(s => s.Value));
        // … but with no ambient transaction no writer was registered, so nothing was ever persisted.
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public async Task MultipleUpdatesInOneTransaction_PersistExactlyOnceOnCommit()
    {
        var storage = new FakeGrainStorage();
        var coordinator = new TransactionCoordinator();
        TransactionalState<Counter> state = MakeState(storage, coordinator);

        Guid id = coordinator.BeginTransaction();
        await state.PerformUpdate(s => s.Value += 1);
        await state.PerformUpdate(s => s.Value += 1);
        await coordinator.CommitAsync(id);

        // Observable invariant: two updates in one transaction → exactly one write-through.
        Assert.Equal(1, storage.WriteCount);
        Assert.Equal(2, storage.LastWritten);
    }

    [Fact]
    public async Task EnsurePending_RegistersWriterExactlyOncePerTransaction()
    {
        // White-box guard for the EnsurePending dedup (_registeredForTxId == txId skip). The
        // double-write that a duplicate registration would cause is otherwise masked at commit time
        // by CommitAsync's own `_pending == null` guard, so storage write-count cannot isolate it —
        // we observe the registered-writer count on the coordinator directly instead.
        var storage = new FakeGrainStorage();
        var coordinator = new TransactionCoordinator();
        TransactionalState<Counter> state = MakeState(storage, coordinator);

        Guid id = coordinator.BeginTransaction();
        await state.PerformUpdate(s => s.Value += 1);
        await state.PerformUpdate(s => s.Value += 1);
        await state.PerformUpdate(s => s.Value += 1);

        Assert.Equal(1, RegisteredWriterCount(coordinator, id));
    }

    [Fact]
    public async Task Commit_PersistsPending_AndSubsequentReadReturnsCommitted()
    {
        var storage = new FakeGrainStorage();
        var coordinator = new TransactionCoordinator();
        TransactionalState<Counter> state = MakeState(storage, coordinator);

        Guid id = coordinator.BeginTransaction();
        await state.PerformUpdate(s => s.Value = 7);
        await coordinator.CommitAsync(id);

        Assert.Equal(7, storage.LastWritten);
        Assert.Equal(7, await state.PerformRead(s => s.Value)); // pending cleared → reads committed
    }

    [Fact]
    public async Task Abort_RollsBackPending_LeavingCommittedUnchanged_AndNothingPersisted()
    {
        var storage = new FakeGrainStorage();
        var coordinator = new TransactionCoordinator();
        TransactionalState<Counter> state = MakeState(storage, coordinator);

        Guid id = coordinator.BeginTransaction();
        await state.PerformUpdate(s => s.Value = 99);
        await coordinator.AbortAsync(id);

        Assert.Equal(0, storage.WriteCount);                    // rollback never persists
        Assert.Equal(0, await state.PerformRead(s => s.Value)); // pending discarded → committed default
    }

    // =====================================================================
    // Fixtures
    // =====================================================================

    /// <summary>Reads the number of writers registered against <paramref name="txId"/> via reflection.</summary>
    private static int RegisteredWriterCount(TransactionCoordinator coordinator, Guid txId)
    {
        FieldInfo field = typeof(TransactionCoordinator)
            .GetField("_transactions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var transactions = (IDictionary)field.GetValue(coordinator)!;
        object? ctx = transactions[txId];
        if (ctx is null) return 0;
        var writers = (ICollection)ctx.GetType().GetProperty("Writers")!.GetValue(ctx)!;
        return writers.Count;
    }

    private static TransactionalState<Counter> MakeState(IGrainStorage storage, TransactionCoordinator coordinator)
        => new(
            "balance",
            new GrainId(new GrainType("Acct"), "1"),
            storage,
            coordinator,
            src => new Counter { Value = src.Value });

    private sealed class Counter
    {
        public int Value { get; set; }
    }

    /// <summary>Minimal in-memory <see cref="IGrainStorage"/> that records read/write activity.</summary>
    private sealed class FakeGrainStorage : IGrainStorage
    {
        private readonly Dictionary<string, object> _store = new();

        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int LastWritten { get; private set; }

        public Task ReadStateAsync<TState>(string stateName, GrainId grainId, GrainState<TState> grainState,
            CancellationToken cancellationToken = default) where TState : new()
        {
            ReadCount++;
            if (_store.TryGetValue(Key(stateName, grainId), out object? saved))
            {
                grainState.State = (TState)saved;
                grainState.RecordExists = true;
            }

            return Task.CompletedTask;
        }

        public Task WriteStateAsync<TState>(string stateName, GrainId grainId, GrainState<TState> grainState,
            CancellationToken cancellationToken = default) where TState : new()
        {
            WriteCount++;
            _store[Key(stateName, grainId)] = grainState.State!;
            if (grainState.State is Counter c) LastWritten = c.Value;
            return Task.CompletedTask;
        }

        public Task ClearStateAsync<TState>(string stateName, GrainId grainId, GrainState<TState> grainState,
            CancellationToken cancellationToken = default) where TState : new()
        {
            _store.Remove(Key(stateName, grainId));
            return Task.CompletedTask;
        }

        private static string Key(string stateName, GrainId grainId) => $"{stateName}/{grainId}";
    }
}
