using System.Collections.Concurrent;
using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Persistence.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IGrainStorage"/> intended for development and tests.
/// State is copied on both read and write to preserve Orleans-style isolation semantics.
/// </summary>
public sealed class InMemoryGrainStorage : IGrainStorage
{
    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);
    private readonly ICopierProvider _copiers;

    /// <summary>Initializes the in-memory storage provider.</summary>
    public InMemoryGrainStorage(ICopierProvider copiers)
    {
        _copiers = copiers;
    }

    /// <inheritdoc/>
    public Task ReadStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new()
    {
        cancellationToken.ThrowIfCancellationRequested();

        string key = GetStorageKey<TState>(stateName, grainId);
        if (_store.TryGetValue(key, out Entry? entry) && entry.Value is TState typed)
        {
            grainState.State = Copy(typed);
            grainState.RecordExists = true;
            grainState.ETag = entry.ETag;
        }
        else
        {
            grainState.State = new TState();
            grainState.RecordExists = false;
            grainState.ETag = string.Empty;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task WriteStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new()
    {
        cancellationToken.ThrowIfCancellationRequested();

        string key = GetStorageKey<TState>(stateName, grainId);
        string eTag = Guid.NewGuid().ToString("N");
        TState copy = Copy(grainState.State);
        _store[key] = new Entry(copy!, eTag);

        grainState.State = Copy(copy);
        grainState.RecordExists = true;
        grainState.ETag = eTag;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ClearStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new()
    {
        cancellationToken.ThrowIfCancellationRequested();

        string key = GetStorageKey<TState>(stateName, grainId);
        _store.TryRemove(key, out _);
        grainState.State = new TState();
        grainState.RecordExists = false;
        grainState.ETag = string.Empty;
        return Task.CompletedTask;
    }

    private TState Copy<TState>(TState value)
    {
        IDeepCopier<TState> copier = _copiers.GetRequiredCopier<TState>();
        return copier.DeepCopy(value, new CopyContext());
    }

    private static string GetStorageKey<TState>(string stateName, GrainId grainId) =>
        $"{grainId.Type.Value}|{grainId.Key}|{stateName}|{typeof(TState).AssemblyQualifiedName}";

    private sealed record Entry(object Value, string ETag);
}