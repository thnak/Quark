namespace Quark.Abstractions;

/// <summary>
/// Interface for actors that can supervise child actors.
/// </summary>
public interface ISupervisor : IActor
{
    /// <summary>
    /// Called when a child actor fails.
    /// </summary>
    /// <param name="context">Context information about the child failure.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A supervision directive indicating how to handle the failure.</returns>
    Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Spawns a new child actor under this supervisor.
    /// </summary>
    /// <typeparam name="TChild">The type of child actor to spawn.</typeparam>
    /// <param name="actorId">The unique identifier for the child actor.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The spawned child actor instance.</returns>
    Task<TChild> SpawnChildAsync<TChild>(
        string actorId,
        CancellationToken cancellationToken = default) where TChild : IActor;

    /// <summary>
    /// Gets all child actors currently supervised by this actor.
    /// </summary>
    /// <returns>A read-only collection of child actors.</returns>
    IReadOnlyCollection<IActor> GetChildren();
}
