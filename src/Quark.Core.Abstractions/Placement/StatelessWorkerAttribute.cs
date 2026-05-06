namespace Quark.Core.Abstractions.Placement;

/// <summary>Applies <see cref="StatelessWorkerPlacement" /> to a grain class.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class StatelessWorkerAttribute : Attribute
{
    /// <summary>Applies stateless-worker placement with the given max-local activations hint.</summary>
    public StatelessWorkerAttribute(int maxLocalWorkers = -1)
    {
        MaxLocalWorkers = maxLocalWorkers;
    }

    /// <inheritdoc cref="StatelessWorkerPlacement.MaxLocalWorkers" />
    public int MaxLocalWorkers { get; }
}
