using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions;

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