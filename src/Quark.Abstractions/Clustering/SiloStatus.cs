namespace Quark.Abstractions.Clustering;

/// <summary>
///     Represents the status of a silo in the cluster.
/// </summary>
public enum SiloStatus
{
    /// <summary>
    ///     The silo is joining the cluster.
    /// </summary>
    Joining,

    /// <summary>
    ///     The silo is active and processing requests.
    /// </summary>
    Active,

    /// <summary>
    ///     The silo is shutting down gracefully.
    /// </summary>
    ShuttingDown,

    /// <summary>
    ///     The silo is dead or unreachable.
    /// </summary>
    Dead
}