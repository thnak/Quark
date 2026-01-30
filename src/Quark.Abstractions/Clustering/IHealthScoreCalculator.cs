namespace Quark.Abstractions.Clustering;

/// <summary>
///     Interface for calculating health scores for silos.
///     Allows custom health score algorithms to be implemented.
/// </summary>
public interface IHealthScoreCalculator
{
    /// <summary>
    ///     Calculates a health score for a silo based on its current metrics.
    /// </summary>
    /// <param name="cpuUsagePercent">CPU usage percentage (0-100).</param>
    /// <param name="memoryUsagePercent">Memory usage percentage (0-100).</param>
    /// <param name="networkLatencyMs">Network latency in milliseconds.</param>
    /// <returns>A health score object containing the calculated metrics.</returns>
    SiloHealthScore CalculateHealthScore(
        double cpuUsagePercent,
        double memoryUsagePercent,
        double networkLatencyMs);

    /// <summary>
    ///     Analyzes a series of health scores to detect trends and predict failures.
    /// </summary>
    /// <param name="historicalScores">Historical health scores in chronological order.</param>
    /// <returns>True if a failure is predicted; otherwise, false.</returns>
    bool PredictFailure(IReadOnlyList<SiloHealthScore> historicalScores);

    /// <summary>
    ///     Detects gradual degradation in health scores over time.
    /// </summary>
    /// <param name="historicalScores">Historical health scores in chronological order.</param>
    /// <returns>True if gradual degradation is detected; otherwise, false.</returns>
    bool DetectGradualDegradation(IReadOnlyList<SiloHealthScore> historicalScores);
}
