using Microsoft.Extensions.DependencyInjection;

namespace Quark.Persistence.Abstractions;

/// <summary>
/// Orleans-style persistent grain base class.
/// State is automatically loaded during activation and can be written using
/// <see cref="WriteStateAsync"/>.
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
        IStorage<TState> storage = ServiceProviderServiceExtensions.GetRequiredService<IStorage<TState>>(ServiceProvider);
        State = await storage.ReadAsync(GrainId, _stateName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual Task WriteStateAsync(CancellationToken cancellationToken = default)
    {
        IStorage<TState> storage = ServiceProviderServiceExtensions.GetRequiredService<IStorage<TState>>(ServiceProvider);
        return storage.WriteAsync(GrainId, State, _stateName, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task ClearStateAsync(CancellationToken cancellationToken = default)
    {
        IStorage<TState> storage = ServiceProviderServiceExtensions.GetRequiredService<IStorage<TState>>(ServiceProvider);
        await storage.ClearAsync(GrainId, _stateName, cancellationToken).ConfigureAwait(false);
        State = new TState();
    }
}
