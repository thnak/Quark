namespace Quark.Abstractions.Clustering;

/// <summary>
///     Event arguments for silo health degradation events.
/// </summary>
public sealed class SiloHealthDegradedEventArgs : EventArgs
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SiloHealthDegradedEventArgs" /> class.
    /// </summary>
    public SiloHealthDegradedEventArgs(
        SiloInfo siloInfo,
        SiloHealthScore healthScore,
        bool predictedFailure)
    {
        SiloInfo = siloInfo ?? throw new ArgumentNullException(nameof(siloInfo));
        HealthScore = healthScore ?? throw new ArgumentNullException(nameof(healthScore));
        PredictedFailure = predictedFailure;
    }

    /// <summary>
    ///     Gets the information about the silo with degraded health.
    /// </summary>
    public SiloInfo SiloInfo { get; }

    /// <summary>
    ///     Gets the current health score.
    /// </summary>
    public SiloHealthScore HealthScore { get; }

    /// <summary>
    ///     Gets a value indicating whether a failure is predicted.
    /// </summary>
    public bool PredictedFailure { get; }
}