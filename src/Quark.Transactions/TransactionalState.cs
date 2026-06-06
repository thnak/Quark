using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;

namespace Quark.Transactions;

public sealed class TransactionalState<TState> : ITransactionalState<TState> where TState : new()
{
    private readonly string _stateName;
    private readonly GrainId _grainId;
    private readonly IGrainStorage _storage;
    private readonly TransactionCoordinator _coordinator;
    private readonly Func<TState, TState> _copyState;

    private TState _committed = new();
    private TState? _pending;
    private Guid _registeredForTxId;

    public TransactionalState(
        string stateName,
        GrainId grainId,
        IGrainStorage storage,
        TransactionCoordinator coordinator,
        Func<TState, TState> copyState)
    {
        _stateName = stateName;
        _grainId = grainId;
        _storage = storage;
        _coordinator = coordinator;
        _copyState = copyState;
    }

    /// <summary>Loads committed state from storage. Call from <c>OnActivateAsync</c>.</summary>
    public async Task LoadAsync()
    {
        var wrapper = new GrainState<TState>();
        await _storage.ReadStateAsync(_stateName, _grainId, wrapper).ConfigureAwait(false);
        _committed = wrapper.State;
    }

    public Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction)
        => Task.FromResult(readFunction(_pending ?? _committed));

    public Task PerformUpdate(Action<TState> updateFunction)
    {
        EnsurePending();
        updateFunction(_pending!);
        return Task.CompletedTask;
    }

    public Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction)
    {
        EnsurePending();
        return Task.FromResult(updateFunction(_pending!));
    }

    private void EnsurePending()
    {
        if (_pending == null)
            _pending = _copyState(_committed);

        if (!_coordinator.IsInTransaction(out var txId)) return;
        if (_registeredForTxId == txId) return;

        _registeredForTxId = txId;
        _coordinator.RegisterWriter(txId, CommitAsync, Rollback);
    }

    private async Task CommitAsync()
    {
        if (_pending == null) return;
        _committed = _pending;
        _pending = default;
        _registeredForTxId = Guid.Empty;
        var wrapper = new GrainState<TState> { State = _committed };
        await _storage.WriteStateAsync(_stateName, _grainId, wrapper).ConfigureAwait(false);
    }

    private void Rollback()
    {
        _pending = default;
        _registeredForTxId = Guid.Empty;
    }
}
