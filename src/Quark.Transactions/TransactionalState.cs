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
    private bool _isLoaded;

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

    /// <summary>Loads committed state from storage. Call from <c>OnActivateAsync</c> or lazily on first use.</summary>
    public async Task LoadAsync()
    {
        var wrapper = new GrainState<TState>();
        await _storage.ReadStateAsync(_stateName, _grainId, wrapper).ConfigureAwait(false);
        _committed = wrapper.State;
        _isLoaded = true;
    }

    public async Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction)
    {
        if (!_isLoaded) await LoadAsync().ConfigureAwait(false);
        return readFunction(_pending ?? _committed);
    }

    public async Task PerformUpdate(Action<TState> updateFunction)
    {
        if (!_isLoaded) await LoadAsync().ConfigureAwait(false);
        EnsurePending();
        updateFunction(_pending!);
    }

    public async Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction)
    {
        if (!_isLoaded) await LoadAsync().ConfigureAwait(false);
        EnsurePending();
        return updateFunction(_pending!);
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
