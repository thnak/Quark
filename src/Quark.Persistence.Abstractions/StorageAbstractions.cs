using Quark.Core.Abstractions;

namespace Quark.Persistence.Abstractions;

/// <summary>
/// Options controlling grain state storage behavior.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>The default logical state name used by persistent grains.</summary>
    public const string DefaultStateName = "Default";

    /// <summary>
    /// Provider name reserved for the default storage provider.
    /// This mirrors Orleans' default grain storage concept.
    /// </summary>
    public string DefaultProviderName { get; set; } = "Default";
}

/// <summary>
/// Mutable container holding a grain's persisted state snapshot and metadata.
/// Modeled after Orleans' <c>GrainState&lt;T&gt;</c> contract.
/// </summary>
public sealed class GrainState<TState> where TState : new()
{
    /// <summary>The current state value.</summary>
    public TState State { get; set; } = new();

    /// <summary>Opaque version/ETag token for concurrency-aware providers.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Whether a backing record existed when the state was read.</summary>
    public bool RecordExists { get; set; }
}

/// <summary>
/// Provider-level abstraction for reading and writing grain state.
/// Compatible with the Orleans mental model of named grain storage providers.
/// </summary>
public interface IGrainStorage
{
    /// <summary>Loads state into <paramref name="grainState"/> for the supplied grain.</summary>
    Task ReadStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new();

    /// <summary>Writes <paramref name="grainState"/> for the supplied grain.</summary>
    Task WriteStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new();

    /// <summary>Clears any persisted state for the supplied grain.</summary>
    Task ClearStateAsync<TState>(
        string stateName,
        GrainId grainId,
        GrainState<TState> grainState,
        CancellationToken cancellationToken = default)
        where TState : new();
}

/// <summary>
/// Typed storage abstraction for a grain's state payload.
/// This is the simplest API for persistent grains and tests.
/// </summary>
public interface IStorage<TState> where TState : new()
{
    /// <summary>Reads the persisted state for <paramref name="grainId"/>.</summary>
    Task<TState> ReadAsync(
        GrainId grainId,
        string? stateName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="state"/> for <paramref name="grainId"/>.</summary>
    Task WriteAsync(
        GrainId grainId,
        TState state,
        string? stateName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the persisted state for <paramref name="grainId"/>.</summary>
    Task ClearAsync(
        GrainId grainId,
        string? stateName = null,
        CancellationToken cancellationToken = default);
}
