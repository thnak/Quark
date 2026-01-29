using System.Collections.Concurrent;
using Quark.Abstractions.Persistence;

namespace Quark.Core.Persistence;

/// <summary>
///     In-memory implementation of state storage for testing and development.
/// </summary>
public class InMemoryStateStorage<TState> : IStateStorage<TState> where TState : class
{
    private readonly ConcurrentDictionary<string, TState> _storage = new();

    /// <inheritdoc />
    public Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        _storage.TryGetValue(key, out var state);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        _storage[key] = state;
        return Task.CompletedTask;
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