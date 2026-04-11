namespace Quark.Core.Abstractions;

/// <summary>
/// Stateless-worker placement: multiple activations of the same grain may exist
/// concurrently on the same silo. The runtime load-balances across them.
/// </summary>
public sealed class StatelessWorkerPlacement : PlacementStrategy
{
    /// <summary>Creates a stateless-worker strategy with the given max-local activations hint.</summary>
    public StatelessWorkerPlacement(int maxLocalWorkers = -1)
    {
        MaxLocalWorkers = maxLocalWorkers;
    }

    /// <summary>
    /// Maximum number of concurrent local activations.
    /// -1 means the runtime chooses automatically (typically number of CPU cores).
    /// </summary>
    public int MaxLocalWorkers { get; }
}

/// <summary>Applies <see cref="StatelessWorkerPlacement"/> to a grain class.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class StatelessWorkerAttribute : Attribute
{
    /// <inheritdoc cref="StatelessWorkerPlacement.MaxLocalWorkers"/>
    public int MaxLocalWorkers { get; }

    /// <summary>Applies stateless-worker placement with the given max-local activations hint.</summary>
    public StatelessWorkerAttribute(int maxLocalWorkers = -1)
    {
        MaxLocalWorkers = maxLocalWorkers;
    }
}
