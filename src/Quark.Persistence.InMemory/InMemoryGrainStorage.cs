using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Core.Abstractions;
using Quark.Persistence.Abstractions;
using Quark.Serialization.Abstractions;

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

/// <summary>
/// Typed facade over <see cref="IGrainStorage"/> for a single state type.
/// </summary>
public sealed class InMemoryStorage<TState> : IStorage<TState>
    where TState : new()
{
    private readonly IGrainStorage _storage;

    /// <summary>Initializes a typed in-memory storage adapter.</summary>
    public InMemoryStorage(IGrainStorage storage)
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

/// <summary>
/// Service registration helpers for the in-memory persistence provider.
/// </summary>
public static class InMemoryGrainStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory grain storage provider as the default persistence backend.
    /// Orleans-compatible alias: <c>AddMemoryGrainStorage()</c>.
    /// </summary>
    public static IServiceCollection AddInMemoryGrainStorage(
        this IServiceCollection services,
        Action<StorageOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IGrainStorage, InMemoryGrainStorage>();
        services.TryAddSingleton(typeof(IStorage<>), typeof(InMemoryStorage<>));
        return services;
    }

    /// <summary>
    /// Orleans-compatible alias for registering an in-memory grain storage provider.
    /// </summary>
    public static IServiceCollection AddMemoryGrainStorage(
        this IServiceCollection services,
        Action<StorageOptions>? configure = null) =>
        services.AddInMemoryGrainStorage(configure);
}
