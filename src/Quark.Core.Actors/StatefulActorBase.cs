using Microsoft.Extensions.DependencyInjection;
using Quark.Abstractions;
using Quark.Abstractions.Persistence;

namespace Quark.Core.Actors;

/// <summary>
///     Base class for actors with state persistence support.
/// </summary>
public abstract class StatefulActorBase : ActorBase
{
    private readonly IStateStorageProvider? _storageProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StatefulActorBase" /> class.
    /// </summary>
    protected StatefulActorBase(string actorId) : base(actorId)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="StatefulActorBase" /> class with an actor factory.
    /// </summary>
    protected StatefulActorBase(string actorId, IActorFactory? actorFactory) : base(actorId, actorFactory)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="StatefulActorBase" /> class with DI scope.
    /// </summary>
    protected StatefulActorBase(string actorId, IActorFactory? actorFactory, IServiceScope? serviceScope)
        : base(actorId, actorFactory, serviceScope)
    {
        _storageProvider = serviceScope?.ServiceProvider.GetService<IStateStorageProvider>();
    }

    /// <summary>
    ///     Gets a storage instance for the specified provider and state type.
    ///     Used by generated code to access state storage.
    /// </summary>
    protected IStateStorage<TState> GetStorage<TState>(string providerName) where TState : class
    {
        if (_storageProvider == null)
            throw new InvalidOperationException(
                "State storage provider is not available. " +
                "Ensure the actor is created with dependency injection support and " +
                "IStateStorageProvider is registered in the service container.");

        return _storageProvider.GetStorage<TState>(providerName);
    }
}