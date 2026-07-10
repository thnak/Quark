using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Persistence.Abstractions.Journaling;

namespace Quark.Persistence.InMemory;

/// <summary>Service registration helpers for the in-memory snapshot store.</summary>
public static class InMemorySnapshotStoreServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the in-memory <see cref="ISnapshotStore" />. Once registered, every
    ///     <see cref="JournaledGrain{TState,TEvent}" /> with a positive <c>SnapshotInterval</c>
    ///     writes snapshots and replays only post-snapshot events on activation.
    /// </summary>
    public static IServiceCollection AddInMemorySnapshotStore(this IServiceCollection services)
    {
        services.TryAddSingleton<ISnapshotStore, InMemorySnapshotStore>();
        return services;
    }
}
