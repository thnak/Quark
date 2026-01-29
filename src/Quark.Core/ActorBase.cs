using System.Collections.Concurrent;

namespace Quark.Core;

/// <summary>
/// Base class for actor implementations.
/// Provides common functionality for all actors, including supervision support.
/// </summary>
public abstract class ActorBase : ISupervisor
{
    private readonly ConcurrentDictionary<string, IActor> _children = new();
    private readonly IActorFactory? _actorFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorBase"/> class.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    protected ActorBase(string actorId) : this(actorId, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorBase"/> class with an actor factory.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    /// <param name="actorFactory">The actor factory for spawning child actors.</param>
    protected ActorBase(string actorId, IActorFactory? actorFactory)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        _actorFactory = actorFactory;
    }

    /// <inheritdoc />
    public string ActorId { get; }

    /// <summary>
    /// Called when the actor is activated.
    /// Override this method to perform initialization logic.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public virtual Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the actor is deactivated.
    /// Override this method to perform cleanup logic.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public virtual Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a child actor fails.
    /// Default implementation restarts the child actor.
    /// Override this method to customize failure handling behavior.
    /// </summary>
    /// <param name="context">Context information about the child failure.</param>
    /// <param name="cancellationToken">A cancellation token. Not used in the default implementation.</param>
    /// <returns>A supervision directive indicating how to handle the failure.</returns>
    public virtual Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Default supervision strategy: restart the failed child
        return Task.FromResult(SupervisionDirective.Restart);
    }

    /// <summary>
    /// Spawns a new child actor under this supervisor.
    /// </summary>
    /// <typeparam name="TChild">The type of child actor to spawn.</typeparam>
    /// <param name="actorId">The unique identifier for the child actor.</param>
    /// <param name="cancellationToken">A cancellation token. Not used in the current implementation.</param>
    /// <returns>The spawned child actor instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the actor was not created with an IActorFactory.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a child with the same actorId already exists.</exception>
    public virtual Task<TChild> SpawnChildAsync<TChild>(
        string actorId,
        CancellationToken cancellationToken = default) where TChild : IActor
    {
        if (_actorFactory == null)
        {
            throw new InvalidOperationException(
                "Cannot spawn child actors without an IActorFactory. " +
                "Ensure the actor is created with an IActorFactory instance.");
        }

        // Validate actorId before checking for duplicates (CreateActor will validate, but we check early)
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor ID cannot be null or whitespace.", nameof(actorId));
        }

        // Check if a child with this ID already exists
        if (_children.ContainsKey(actorId))
        {
            throw new InvalidOperationException(
                $"A child actor with ID '{actorId}' already exists. " +
                "Each child actor must have a unique ID within its supervisor.");
        }

        var child = _actorFactory.CreateActor<TChild>(actorId);
        _children[actorId] = child;

        return Task.FromResult(child);
    }

    /// <summary>
    /// Gets all child actors currently supervised by this actor.
    /// Returns a snapshot of the children at the time of the call.
    /// </summary>
    /// <returns>A read-only collection of child actors representing a snapshot at a point in time.</returns>
    public virtual IReadOnlyCollection<IActor> GetChildren()
    {
        return _children.Values.ToArray();
    }
}
