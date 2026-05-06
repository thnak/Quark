using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions;

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