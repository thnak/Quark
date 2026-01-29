using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
///     Base class for actor implementations with supervision and DI support.
/// </summary>
public abstract class ActorBase : ISupervisor, IDisposable
{
    private readonly IActorFactory? _actorFactory;
    private readonly ConcurrentDictionary<string, IActor> _children = new();
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorBase" /> class.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    protected ActorBase(string actorId) : this(actorId, null, null)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorBase" /> class with an actor factory.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    /// <param name="actorFactory">The actor factory for spawning child actors.</param>
    protected ActorBase(string actorId, IActorFactory? actorFactory) : this(actorId, actorFactory, null)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorBase" /> class with DI scope.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    /// <param name="actorFactory">The actor factory for spawning child actors.</param>
    /// <param name="serviceScope">The service scope for dependency injection.</param>
    protected ActorBase(string actorId, IActorFactory? actorFactory, IServiceScope? serviceScope)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        _actorFactory = actorFactory;
        ServiceScope = serviceScope;
    }

    /// <summary>
    ///     Gets the service scope for this actor instance.
    /// </summary>
    protected IServiceScope? ServiceScope { get; }

    /// <summary>
    ///     Disposes the actor and its service scope.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public string ActorId { get; }

    /// <inheritdoc />
    public virtual Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Default supervision strategy: restart the failed child
        return Task.FromResult(SupervisionDirective.Restart);
    }

    /// <inheritdoc />
    public virtual Task<TChild> SpawnChildAsync<TChild>(
        string actorId,
        CancellationToken cancellationToken = default) where TChild : IActor
    {
        if (_actorFactory == null)
            throw new InvalidOperationException(
                "Cannot spawn child actors without an IActorFactory. " +
                "Ensure the actor is created with an IActorFactory instance.");

        // Validate actorId
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Actor ID cannot be null or whitespace.", nameof(actorId));

        // Check if a child with this ID already exists
        if (_children.ContainsKey(actorId))
            throw new InvalidOperationException(
                $"A child actor with ID '{actorId}' already exists. " +
                "Each child actor must have a unique ID within its supervisor.");

        var child = _actorFactory.CreateActor<TChild>(actorId);
        _children[actorId] = child;

        return Task.FromResult(child);
    }

    /// <inheritdoc />
    public virtual IReadOnlyCollection<IActor> GetChildren()
    {
        return _children.Values.ToArray();
    }

    /// <summary>
    ///     Gets a service from the DI container.
    /// </summary>
    /// <typeparam name="TService">The type of service to retrieve.</typeparam>
    /// <returns>The service instance, or null if not available.</returns>
    protected TService? GetService<TService>() where TService : class
    {
        return ServiceScope?.ServiceProvider.GetService<TService>();
    }

    /// <summary>
    ///     Gets a required service from the DI container.
    /// </summary>
    /// <typeparam name="TService">The type of service to retrieve.</typeparam>
    /// <returns>The service instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the service is not available.</exception>
    protected TService GetRequiredService<TService>() where TService : class
    {
        if (ServiceScope == null)
            throw new InvalidOperationException(
                "Cannot get services without a service scope. " +
                "Ensure the actor is created with dependency injection support.");
        return ServiceScope.ServiceProvider.GetRequiredService<TService>();
    }

    /// <summary>
    ///     Disposes the actor and its service scope.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing) ServiceScope?.Dispose();

        _disposed = true;
    }
}