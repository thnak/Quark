using Quark.Core.Abstractions;
using Quark.Persistence.Abstractions;

namespace Quark.Persistence.Redis;

/// <summary>
/// Typed facade over <see cref="RedisGrainStorage"/> for a single state type.
/// </summary>
public sealed class RedisStorage<TState> : IStorage<TState>
    where TState : new()
{
    private readonly IGrainStorage _storage;

    /// <summary>Creates a typed Redis storage adapter.</summary>
    public RedisStorage(IGrainStorage storage)
    {
        _storage = storage;
    }

    /// <inheritdoc/>
    public async Task<TState> ReadAsync(
        GrainId grainId,
        string? stateName = null,
        CancellationToken cancellationToken = default)
    {
        GrainState<TState> state = new();
        await _storage.ReadStateAsync(
            stateName ?? StorageOptions.DefaultStateName,
            grainId,
            state,
            cancellationToken).ConfigureAwait(false);

        return state.State;
    }

    /// <inheritdoc/>
    public Task WriteAsync(
        GrainId grainId,
        TState state,
        string? stateName = null,
        CancellationToken cancellationToken = default)
    {
        GrainState<TState> grainState = new() { State = state };
        return _storage.WriteStateAsync(
            stateName ?? StorageOptions.DefaultStateName,
            grainId,
            grainState,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task ClearAsync(
        GrainId grainId,
        string? stateName = null,
        CancellationToken cancellationToken = default)
    {
        GrainState<TState> grainState = new();
        return _storage.ClearStateAsync(
            stateName ?? StorageOptions.DefaultStateName,
            grainId,
            grainState,
            cancellationToken);
    }
}