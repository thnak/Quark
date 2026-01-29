using System.Collections.Concurrent;
using Quark.Abstractions.Persistence;

namespace Quark.Core.Persistence;

/// <summary>
///     In-memory implementation of state storage for testing and development.
///     Supports optimistic concurrency control.
/// </summary>
public class InMemoryStateStorage<TState> : IStateStorage<TState> where TState : class
{
    private readonly ConcurrentDictionary<string, (TState State, long Version)> _storage = new();

    /// <inheritdoc />
    public Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        if (_storage.TryGetValue(key, out var entry))
        {
            return Task.FromResult<TState?>(entry.State);
        }
        return Task.FromResult<TState?>(null);
    }

    /// <inheritdoc />
    public Task<StateWithVersion<TState>?> LoadWithVersionAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        if (_storage.TryGetValue(key, out var entry))
        {
            return Task.FromResult<StateWithVersion<TState>?>(new StateWithVersion<TState>(entry.State, entry.Version));
        }
        return Task.FromResult<StateWithVersion<TState>?>(null);
    }

    /// <inheritdoc />
    public Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        var version = _storage.TryGetValue(key, out var existing) ? existing.Version + 1 : 1;
        _storage[key] = (state, version);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> SaveWithVersionAsync(string actorId, string stateName, TState state, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        
        if (expectedVersion == null)
        {
            // First save - version will be 1
            var newVersion = 1L;
            _storage[key] = (state, newVersion);
            return Task.FromResult(newVersion);
        }
        
        // Check version and update atomically
        var updated = false;
        var resultVersion = 0L;
        
        _storage.AddOrUpdate(
            key,
            // If key doesn't exist but we expected a version, that's a conflict
            _ => throw new ConcurrencyException(expectedVersion.Value, 0),
            // If key exists, check version
            (_, existing) =>
            {
                if (existing.Version != expectedVersion.Value)
                {
                    throw new ConcurrencyException(expectedVersion.Value, existing.Version);
                }
                resultVersion = existing.Version + 1;
                updated = true;
                return (state, resultVersion);
            });
        
        if (!updated && expectedVersion != null)
        {
            throw new ConcurrencyException(expectedVersion.Value, 0);
        }
        
        return Task.FromResult(resultVersion);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        _storage.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static string GetKey(string actorId, string stateName)
    {
        return $"{actorId}:{stateName}";
    }
}