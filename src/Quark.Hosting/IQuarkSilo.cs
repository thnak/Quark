using Quark.Abstractions;
using Quark.Abstractions.Clustering;

namespace Quark.Hosting;

/// <summary>
/// Represents a Quark silo that hosts actors and manages cluster membership.
/// The silo orchestrates the lifecycle of all subsystems including actors, reminders, and streaming.
/// </summary>
public interface IQuarkSilo : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this silo.
    /// </summary>
    string SiloId { get; }

    /// <summary>
    /// Gets the current status of the silo.
    /// </summary>
    SiloStatus Status { get; }

    /// <summary>
    /// Gets the silo information.
    /// </summary>
    SiloInfo SiloInfo { get; }

    /// <summary>
    /// Gets the actor factory for creating and retrieving actors.
    /// </summary>
    IActorFactory ActorFactory { get; }

    /// <summary>
    /// Starts the silo and all managed subsystems.
    /// Transitions silo status: Joining → Active
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the silo gracefully with proper cleanup.
    /// Transitions silo status: Active → ShuttingDown → Dead
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active actors on this silo.
    /// </summary>
    IReadOnlyCollection<IActor> GetActiveActors();
}
