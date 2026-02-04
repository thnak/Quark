namespace Quark.Abstractions.Clustering;

/// <summary>
/// Represents the result of a routing decision.
/// </summary>
public enum RoutingResult
{
    /// <summary>
    /// Actor is located on a remote silo, requires network call.
    /// </summary>
    Remote,

    /// <summary>
    /// Actor is on the same silo, can be invoked locally.
    /// </summary>
    LocalSilo,

    /// <summary>
    /// Actor is in the same process, can be invoked directly.
    /// </summary>
    SameProcess,

    /// <summary>
    /// Routing target not found or unavailable.
    /// </summary>
    NotFound
}