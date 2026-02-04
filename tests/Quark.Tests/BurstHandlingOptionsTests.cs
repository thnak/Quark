using Quark.Abstractions;

namespace Quark.Tests;

/// <summary>
///     Phase 8.3: Tests for burst handling options.
/// </summary>
public class BurstHandlingOptionsTests
{
    [Fact]
    public void AdaptiveMailboxOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new AdaptiveMailboxOptions();

        // Assert
        Assert.Equal(1000, options.InitialCapacity);
        Assert.Equal(100, options.MinCapacity);
        Assert.Equal(10000, options.MaxCapacity);
        Assert.Equal(0.8, options.GrowThreshold);
        Assert.Equal(0.2, options.ShrinkThreshold);
        Assert.Equal(2.0, options.GrowthFactor);
        Assert.Equal(0.5, options.ShrinkFactor);
        Assert.Equal(10, options.MinSamplesBeforeAdapt);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void CircuitBreakerOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new CircuitBreakerOptions();

        // Assert
        Assert.Equal(5, options.FailureThreshold);
        Assert.Equal(3, options.SuccessThreshold);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.SamplingWindow);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void RateLimitOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new RateLimitOptions();

        // Assert
        Assert.Equal(1000, options.MaxMessagesPerWindow);
        Assert.Equal(TimeSpan.FromSeconds(1), options.TimeWindow);
        Assert.Equal(RateLimitAction.Drop, options.ExcessAction);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void CircuitState_Values_AreCorrect()
    {
        // Assert
        Assert.Equal(0, (int)CircuitState.Closed);
        Assert.Equal(1, (int)CircuitState.Open);
        Assert.Equal(2, (int)CircuitState.HalfOpen);
    }

    [Fact]
    public void RateLimitAction_Values_AreCorrect()
    {
        // Assert
        Assert.Equal(0, (int)RateLimitAction.Drop);
        Assert.Equal(1, (int)RateLimitAction.Reject);
        Assert.Equal(2, (int)RateLimitAction.Queue);
    }
}