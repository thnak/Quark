using Quark.Abstractions.Clustering;

namespace Quark.Tests;

/// <summary>
///     Tests for SiloHealthScore and health score calculation.
/// </summary>
public class SiloHealthScoreTests
{
    [Fact]
    public void SiloHealthScore_CalculatesOverallScore()
    {
        // Arrange & Act
        var healthScore = new SiloHealthScore(
            cpuUsagePercent: 30,
            memoryUsagePercent: 40,
            networkLatencyMs: 50,
            timestamp: DateTimeOffset.UtcNow);

        // Assert
        // CPU score: 70 (100-30)
        // Memory score: 60 (100-40)
        // Latency score: 95 (100 - 50/10)
        // Weighted: (70 * 0.3) + (60 * 0.3) + (95 * 0.4) = 21 + 18 + 38 = 77
        Assert.InRange(healthScore.OverallScore, 76, 78);
    }

    [Fact]
    public void SiloHealthScore_ClampsValues()
    {
        // Arrange & Act
        var healthScore = new SiloHealthScore(
            cpuUsagePercent: 150, // Should be clamped to 100
            memoryUsagePercent: -10, // Should be clamped to 0
            networkLatencyMs: -5, // Should be clamped to 0
            timestamp: DateTimeOffset.UtcNow);

        // Assert
        Assert.Equal(100, healthScore.CpuUsagePercent);
        Assert.Equal(0, healthScore.MemoryUsagePercent);
        Assert.Equal(0, healthScore.NetworkLatencyMs);
    }

    [Fact]
    public void IsHealthy_ReturnsTrueWhenAboveThreshold()
    {
        // Arrange
        var healthScore = new SiloHealthScore(20, 20, 50, DateTimeOffset.UtcNow);

        // Act & Assert
        Assert.True(healthScore.IsHealthy(50)); // Score is ~77, above 50
    }

    [Fact]
    public void IsHealthy_ReturnsFalseWhenBelowThreshold()
    {
        // Arrange
        var healthScore = new SiloHealthScore(90, 90, 500, DateTimeOffset.UtcNow);

        // Act & Assert
        Assert.False(healthScore.IsHealthy(50)); // Score is low, below 50
    }

    [Theory]
    [InlineData(0, 0, 0, 100)] // Perfect health
    [InlineData(100, 100, 1000, 0)] // Worst health
    [InlineData(50, 50, 100, 70)] // Medium health
    public void OverallScore_CalculatesCorrectly(
        double cpu, 
        double memory, 
        double latency, 
        double expectedScore)
    {
        // Arrange
        var healthScore = new SiloHealthScore(cpu, memory, latency, DateTimeOffset.UtcNow);

        // Act & Assert
        Assert.InRange(healthScore.OverallScore, expectedScore - 5, expectedScore + 5);
    }
}
