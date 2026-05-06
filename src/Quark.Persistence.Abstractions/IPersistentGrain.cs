namespace Quark.Persistence.Abstractions;

/// <summary>
/// Orleans-compatible contract for grains which own a single persisted state object.
/// </summary>
public interface IPersistentGrain<TState> where TState : new()
{
    /// <summary>The current in-memory state for this activation.</summary>
    TState State { get; }

    /// <summary>Loads the latest state from the configured storage provider.</summary>
    Task ReadStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the current <see cref="State"/> to the configured storage provider.</summary>
    Task WriteStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Clears the persisted state and resets <see cref="State"/>.</summary>
    Task ClearStateAsync(CancellationToken cancellationToken = default);
}