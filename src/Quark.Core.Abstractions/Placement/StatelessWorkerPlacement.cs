namespace Quark.Core.Abstractions.Placement;

/// <summary>
///     Stateless-worker placement: multiple activations of the same grain may exist
///     concurrently on the same silo. The runtime load-balances across them.
/// </summary>
public sealed class StatelessWorkerPlacement : PlacementStrategy
{
    /// <summary>Creates a stateless-worker strategy with the given max-local activations hint.</summary>
    public StatelessWorkerPlacement(int maxLocalWorkers = -1)
    {
        MaxLocalWorkers = maxLocalWorkers;
    }

    /// <summary>
    ///     Maximum number of concurrent local activations.
    ///     -1 means the runtime chooses automatically (typically number of CPU cores).
    /// </summary>
    public int MaxLocalWorkers { get; }
}
