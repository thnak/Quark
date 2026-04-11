namespace Quark.Core.Abstractions;

using Microsoft.Extensions.DependencyInjection;
using Quark.Persistence.Abstractions;

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

/// <summary>
/// Orleans-style persistent grain base class.
/// State is automatically loaded during activation and can be written using
/// <see cref="WriteStateAsync(CancellationToken)"/>.
/// </summary>
public abstract class Grain<TState> : Grain, IPersistentGrain<TState>
    where TState : new()
{
    private readonly string _stateName;

    /// <summary>Creates a persistent grain using the default state name.</summary>
    protected Grain(string? stateName = null)
    {
        _stateName = string.IsNullOrWhiteSpace(stateName)
            ? StorageOptions.DefaultStateName
            : stateName;
        State = new TState();
    }

    /// <inheritdoc/>
    public TState State { get; protected set; }

    /// <inheritdoc/>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual async Task ReadStateAsync(CancellationToken cancellationToken = default)
    {
        IStorage<TState> storage = ServiceProvider.GetRequiredService<IStorage<TState>>();
        State = await storage.ReadAsync(GrainId, _stateName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual Task WriteStateAsync(CancellationToken cancellationToken = default)
    {
        IStorage<TState> storage = ServiceProvider.GetRequiredService<IStorage<TState>>();
        return storage.WriteAsync(GrainId, State, _stateName, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task ClearStateAsync(CancellationToken cancellationToken = default)
    {
        IStorage<TState> storage = ServiceProvider.GetRequiredService<IStorage<TState>>();
        await storage.ClearAsync(GrainId, _stateName, cancellationToken).ConfigureAwait(false);
        State = new TState();
    }
}
