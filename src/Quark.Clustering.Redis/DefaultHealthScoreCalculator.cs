using Quark.Abstractions.Clustering;

namespace Quark.Clustering.Redis;

/// <summary>
///     Default implementation of health score calculation with predictive analysis.
/// </summary>
public sealed class DefaultHealthScoreCalculator : IHealthScoreCalculator
{
    /// <inheritdoc />
    public SiloHealthScore CalculateHealthScore(
        double cpuUsagePercent,
        double memoryUsagePercent,
        double networkLatencyMs)
    {
        return new SiloHealthScore(
            cpuUsagePercent,
            memoryUsagePercent,
            networkLatencyMs,
            DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool PredictFailure(IReadOnlyList<SiloHealthScore> historicalScores)
    {
        if (historicalScores.Count < 3)
            return false;

        // Get the last 3 scores
        var recentScores = historicalScores
            .TakeLast(3)
            .Select(s => s.OverallScore)
            .ToList();

        // Predict failure if:
        // 1. All recent scores are declining
        // 2. The latest score is below 40
        var isConsistentlyDeclining = recentScores[1] < recentScores[0] &&
                                      recentScores[2] < recentScores[1];
        var isCriticallyLow = recentScores[2] < 40;

        return isConsistentlyDeclining && isCriticallyLow;
    }

    /// <inheritdoc />
    public bool DetectGradualDegradation(IReadOnlyList<SiloHealthScore> historicalScores)
    {
        if (historicalScores.Count < 5)
            return false;

        // Get the last 5 scores
        var recentScores = historicalScores
            .TakeLast(5)
            .Select(s => s.OverallScore)
            .ToList();

        // Calculate the slope of the trend line
        var n = recentScores.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        for (var i = 0; i < n; i++)
        {
            var x = i;
            var y = recentScores[i];
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);

        // Gradual degradation if slope is negative and significant
        // (more than -3 points per measurement)
        return slope < -3.0;
    }
}
