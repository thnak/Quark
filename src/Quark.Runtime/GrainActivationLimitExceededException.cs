namespace Quark.Runtime;

/// <summary>
///     Thrown when a new grain cannot be activated because the silo has reached its configured
///     <see cref="SiloRuntimeOptions.MaxActivations"/> cap. Signals overload rather than letting a
///     peer exhaust silo memory by addressing an unbounded number of distinct grains.
/// </summary>
public sealed class GrainActivationLimitExceededException : Exception
{
    /// <summary>Initialises the exception for the given <paramref name="grainId"/> and <paramref name="limit"/>.</summary>
    public GrainActivationLimitExceededException(GrainId grainId, int limit)
        : base($"Cannot activate grain '{grainId}': the silo activation limit of {limit} has been reached.")
    {
        GrainId = grainId;
        Limit = limit;
    }

    /// <summary>The grain whose activation was refused.</summary>
    public GrainId GrainId { get; }

    /// <summary>The configured activation cap that was reached.</summary>
    public int Limit { get; }
}
