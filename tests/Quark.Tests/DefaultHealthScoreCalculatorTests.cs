using Quark.Abstractions.Clustering;
using Quark.Clustering.Redis;

namespace Quark.Tests;

/// <summary>
///     Tests for DefaultHealthScoreCalculator.
/// </summary>
public class DefaultHealthScoreCalculatorTests
{
    private readonly DefaultHealthScoreCalculator _calculator = new();

    [Fact]
    public void CalculateHealthScore_ReturnsValidScore()
    {
        // Act
        var score = _calculator.CalculateHealthScore(30, 40, 50);

        // Assert
        Assert.NotNull(score);
        Assert.Equal(30, score.CpuUsagePercent);
        Assert.Equal(40, score.MemoryUsagePercent);
        Assert.Equal(50, score.NetworkLatencyMs);
        Assert.InRange(score.OverallScore, 0, 100);
    }

    [Fact]
    public void PredictFailure_ReturnsFalseWithInsufficientData()
    {
        // Arrange
        var scores = new List<SiloHealthScore>
        {
            new(20, 20, 50, DateTimeOffset.UtcNow),
            new(25, 25, 60, DateTimeOffset.UtcNow)
        };

        // Act
        var result = _calculator.PredictFailure(scores);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void PredictFailure_ReturnsTrueWhenScoresDeclining()
    {
        // Arrange - Create declining health scores
        var now = DateTimeOffset.UtcNow;
        var scores = new List<SiloHealthScore>
        {
            new(20, 20, 50, now.AddMinutes(-3)),  // High score (~77)
            new(50, 50, 200, now.AddMinutes(-2)), // Medium score (~50)
            new(80, 80, 500, now.AddMinutes(-1))  // Low score (~15)
        };

        // Act
        var result = _calculator.PredictFailure(scores);

        // Assert
        Assert.True(result); // Consistently declining and critically low
    }

    [Fact]
    public void PredictFailure_ReturnsFalseWhenScoresImproving()
    {
        // Arrange - Create improving health scores
        var now = DateTimeOffset.UtcNow;
        var scores = new List<SiloHealthScore>
        {
            new(80, 80, 500, now.AddMinutes(-3)), // Low score
            new(50, 50, 200, now.AddMinutes(-2)), // Medium score
            new(20, 20, 50, now.AddMinutes(-1))   // High score
        };

        // Act
        var result = _calculator.PredictFailure(scores);

        // Assert
        Assert.False(result); // Improving, not declining
    }

    [Fact]
    public void DetectGradualDegradation_ReturnsFalseWithInsufficientData()
    {
        // Arrange
        var scores = new List<SiloHealthScore>
        {
            new(20, 20, 50, DateTimeOffset.UtcNow),
            new(25, 25, 60, DateTimeOffset.UtcNow)
        };

        // Act
        var result = _calculator.DetectGradualDegradation(scores);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DetectGradualDegradation_ReturnsTrueWithSteepDecline()
    {
        // Arrange - Create gradually declining scores (more than -3 per measurement)
        var now = DateTimeOffset.UtcNow;
        var scores = new List<SiloHealthScore>
        {
            new(10, 10, 50, now.AddMinutes(-5)),  // Score ~85
            new(20, 20, 100, now.AddMinutes(-4)), // Score ~73
            new(30, 30, 150, now.AddMinutes(-3)), // Score ~61
            new(50, 50, 250, now.AddMinutes(-2)), // Score ~45
            new(70, 70, 400, now.AddMinutes(-1))  // Score ~26
        };

        // Act
        var result = _calculator.DetectGradualDegradation(scores);

        // Assert
        Assert.True(result); // Declining more than 3 points per measurement
    }

    [Fact]
    public void DetectGradualDegradation_ReturnsFalseWithStableScores()
    {
        // Arrange - Create stable scores
        var now = DateTimeOffset.UtcNow;
        var scores = new List<SiloHealthScore>
        {
            new(30, 30, 100, now.AddMinutes(-5)),
            new(32, 32, 105, now.AddMinutes(-4)),
            new(31, 31, 102, now.AddMinutes(-3)),
            new(33, 33, 98, now.AddMinutes(-2)),
            new(30, 30, 100, now.AddMinutes(-1))
        };

        // Act
        var result = _calculator.DetectGradualDegradation(scores);

        // Assert
        Assert.False(result); // Scores are stable
    }

    [Fact]
    public void DetectGradualDegradation_ReturnsFalseWithSlowDecline()
    {
        // Arrange - Create slowly declining scores (less than -3 per measurement)
        var now = DateTimeOffset.UtcNow;
        var scores = new List<SiloHealthScore>
        {
            new(30, 30, 100, now.AddMinutes(-5)), // Score ~70
            new(32, 32, 110, now.AddMinutes(-4)), // Score ~68
            new(34, 34, 120, now.AddMinutes(-3)), // Score ~66
            new(36, 36, 130, now.AddMinutes(-2)), // Score ~64
            new(38, 38, 140, now.AddMinutes(-1))  // Score ~62
        };

        // Act
        var result = _calculator.DetectGradualDegradation(scores);

        // Assert
        Assert.False(result); // Decline is too slow (less than -3 per measurement)
    }
}
