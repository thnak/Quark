namespace Quark.Core.Abstractions.Clustering;

/// <summary>Lifecycle status of a silo in the cluster membership table.</summary>
public enum SiloStatus
{
    /// <summary>Silo entry created but not yet participating in the cluster.</summary>
    Created,

    /// <summary>Silo is in the process of joining the cluster.</summary>
    Joining,

    /// <summary>Silo is fully active and accepting grain activations.</summary>
    Active,

    /// <summary>Silo is gracefully shutting down.</summary>
    ShuttingDown,

    /// <summary>Silo is no longer reachable or has been explicitly stopped.</summary>
    Dead,
}
