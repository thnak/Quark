using Quark.Abstractions;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for actor logging options and sampling configuration.
/// </summary>
public class ActorLoggingOptionsTests
{
    [Fact]
    public void ActorLoggingOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new ActorLoggingOptions();

        // Assert
        Assert.True(options.UseActorScopes);
        Assert.Null(options.GlobalSamplingConfiguration);
        Assert.Empty(options.ActorTypeSamplingConfigurations);
    }

    [Fact]
    public void ActorLoggingOptions_GetSamplingConfiguration_ReturnsGlobalConfig()
    {
        // Arrange
        var globalSampling = new LogSamplingConfiguration { SamplingRate = 0.5 };
        var options = new ActorLoggingOptions
        {
            GlobalSamplingConfiguration = globalSampling
        };

        // Act
        var config = options.GetSamplingConfiguration("UnknownActor");

        // Assert
        Assert.NotNull(config);
        Assert.Same(globalSampling, config);
    }

    [Fact]
    public void ActorLoggingOptions_GetSamplingConfiguration_ReturnsActorSpecificConfig()
    {
        // Arrange
        var globalSampling = new LogSamplingConfiguration { SamplingRate = 0.5 };
        var actorSampling = new LogSamplingConfiguration { SamplingRate = 0.1 };
        var options = new ActorLoggingOptions
        {
            GlobalSamplingConfiguration = globalSampling,
            ActorTypeSamplingConfigurations = new Dictionary<string, LogSamplingConfiguration>
            {
                ["HighVolumeActor"] = actorSampling
            }
        };

        // Act
        var config = options.GetSamplingConfiguration("HighVolumeActor");

        // Assert
        Assert.NotNull(config);
        Assert.Same(actorSampling, config);
        Assert.Equal(0.1, config.SamplingRate);
    }

    [Fact]
    public void ActorLoggingOptions_GetSamplingConfiguration_ReturnsNullWhenNotConfigured()
    {
        // Arrange
        var options = new ActorLoggingOptions();

        // Act
        var config = options.GetSamplingConfiguration("SomeActor");

        // Assert
        Assert.Null(config);
    }

    [Fact]
    public void LogSamplingConfiguration_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new LogSamplingConfiguration();

        // Assert
        Assert.True(config.Enabled);
        Assert.Equal(0.1, config.SamplingRate);
        Assert.Equal(2, config.MinimumLevelForSampling); // Information level
        Assert.True(config.AlwaysLogErrors);
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_AlwaysTrueWhenDisabled()
    {
        // Arrange
        var config = new LogSamplingConfiguration { Enabled = false };

        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            Assert.True(config.ShouldLog());
        }
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_AlwaysTrueWhenRateIsOne()
    {
        // Arrange
        var config = new LogSamplingConfiguration { SamplingRate = 1.0 };

        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            Assert.True(config.ShouldLog());
        }
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_AlwaysFalseWhenRateIsZero()
    {
        // Arrange
        var config = new LogSamplingConfiguration { SamplingRate = 0.0 };

        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            Assert.False(config.ShouldLog());
        }
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_SamplesApproximatelyCorrectly()
    {
        // Arrange
        var config = new LogSamplingConfiguration { SamplingRate = 0.3 };
        var trueCount = 0;
        var iterations = 10000;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            if (config.ShouldLog())
                trueCount++;
        }

        // Assert - should be approximately 30% (allow 5% margin)
        var actualRate = trueCount / (double)iterations;
        Assert.InRange(actualRate, 0.25, 0.35);
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_WithLevel_AlwaysLogsErrors()
    {
        // Arrange
        var config = new LogSamplingConfiguration 
        { 
            SamplingRate = 0.0, // 0% sampling
            AlwaysLogErrors = true 
        };

        // Act & Assert - Error (4) and Critical (5) should always log
        for (int i = 0; i < 100; i++)
        {
            Assert.True(config.ShouldLog(4)); // Error
            Assert.True(config.ShouldLog(5)); // Critical
        }
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_WithLevel_SamplesInformationAndWarning()
    {
        // Arrange
        var config = new LogSamplingConfiguration 
        { 
            SamplingRate = 0.0, // 0% sampling for Info and above
            MinimumLevelForSampling = 2, // Information
            AlwaysLogErrors = true
        };

        // Act & Assert - Info (2) and Warning (3) should be sampled (return false)
        Assert.False(config.ShouldLog(2)); // Information - sampled
        Assert.False(config.ShouldLog(3)); // Warning - sampled
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_WithLevel_NeverSamplesBelowMinimum()
    {
        // Arrange
        var config = new LogSamplingConfiguration 
        { 
            SamplingRate = 0.0, // 0% sampling
            MinimumLevelForSampling = 2 // Information
        };

        // Act & Assert - Trace (0) and Debug (1) should always log (below minimum)
        for (int i = 0; i < 100; i++)
        {
            Assert.True(config.ShouldLog(0)); // Trace
            Assert.True(config.ShouldLog(1)); // Debug
        }
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_WithLevel_RespectsAlwaysLogErrorsConfig()
    {
        // Arrange
        var config = new LogSamplingConfiguration 
        { 
            SamplingRate = 0.0, // 0% sampling
            AlwaysLogErrors = false // Allow error sampling
        };

        // Act & Assert - Errors should now be sampled (return false)
        Assert.False(config.ShouldLog(4)); // Error - sampled
        Assert.False(config.ShouldLog(5)); // Critical - sampled
    }

    [Fact]
    public void LogSamplingConfiguration_ShouldLog_WithLevel_CombinedLogic()
    {
        // Arrange
        var config = new LogSamplingConfiguration 
        { 
            SamplingRate = 1.0, // 100% sampling
            MinimumLevelForSampling = 2, // Information
            AlwaysLogErrors = true
        };

        // Act & Assert
        // Below minimum - always log
        Assert.True(config.ShouldLog(0)); // Trace
        Assert.True(config.ShouldLog(1)); // Debug
        
        // At or above minimum - 100% sampling means always log
        Assert.True(config.ShouldLog(2)); // Information
        Assert.True(config.ShouldLog(3)); // Warning
        Assert.True(config.ShouldLog(4)); // Error
        Assert.True(config.ShouldLog(5)); // Critical
    }
}
