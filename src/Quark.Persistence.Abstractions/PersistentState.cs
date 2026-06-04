using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions;

/// <summary>
///     Concrete implementation of <see cref="IPersistentState{TState}" />.
///     Wraps a single <see cref="IGrainStorage" /> slot identified by <paramref name="grainId" /> +
///     <paramref name="stateName" />.
/// </summary>
public sealed class PersistentState<TState> : IPersistentState<TState>
    where TState : new()
{
    private readonly GrainId _grainId;
    private readonly string _stateName;
    private readonly IGrainStorage _storage;
    private readonly GrainState<TState> _grainState = new();

    /// <summary>Initialises the state slot.</summary>
    public PersistentState(GrainId grainId, string stateName, IGrainStorage storage)
    {
        _grainId = grainId;
        _stateName = stateName;
        _storage = storage;
    }

    /// <inheritdoc />
    public TState State
    {
        get => _grainState.State;
        set => _grainState.State = value;
    }

    /// <inheritdoc />
    public bool RecordExists => _grainState.RecordExists;

    /// <inheritdoc />
    public Task ReadStateAsync(CancellationToken cancellationToken = default)
        => _storage.ReadStateAsync(_stateName, _grainId, _grainState, cancellationToken);

    /// <inheritdoc />
    public Task WriteStateAsync(CancellationToken cancellationToken = default)
        => _storage.WriteStateAsync(_stateName, _grainId, _grainState, cancellationToken);

    /// <inheritdoc />
    public async Task ClearStateAsync(CancellationToken cancellationToken = default)
    {
        await _storage.ClearStateAsync(_stateName, _grainId, _grainState, cancellationToken).ConfigureAwait(false);
        _grainState.State = new TState();
    }
}
