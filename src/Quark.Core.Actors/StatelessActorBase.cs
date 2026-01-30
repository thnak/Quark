// Copyright (c) Quark Framework. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
/// Base class for stateless worker actors optimized for high-throughput processing.
/// Stateless actors have no state persistence overhead and can run multiple instances
/// per actor ID for load balancing.
/// </summary>
public abstract class StatelessActorBase : ActorBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StatelessActorBase"/> class.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor instance.</param>
    protected StatelessActorBase(string actorId) : base(actorId)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StatelessActorBase"/> class with an actor factory.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor instance.</param>
    /// <param name="actorFactory">The actor factory for spawning child actors.</param>
    protected StatelessActorBase(string actorId, IActorFactory? actorFactory) : base(actorId, actorFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StatelessActorBase"/> class with DI scope.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor instance.</param>
    /// <param name="actorFactory">The actor factory for spawning child actors.</param>
    /// <param name="serviceScope">The service scope for dependency injection.</param>
    protected StatelessActorBase(string actorId, IActorFactory? actorFactory, IServiceScope? serviceScope)
        : base(actorId, actorFactory, serviceScope)
    {
    }

    /// <summary>
    /// Called when the actor is activated.
    /// For stateless actors, this is typically a lightweight initialization.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Stateless actors have minimal activation overhead
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the actor is deactivated.
    /// For stateless actors, this is typically a lightweight cleanup.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        // Stateless actors have minimal deactivation overhead
        return Task.CompletedTask;
    }
}
