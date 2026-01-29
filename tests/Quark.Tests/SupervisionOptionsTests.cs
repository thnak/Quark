using Quark.Abstractions;
using Xunit;

namespace Quark.Tests;

public class SupervisionOptionsTests
{
    [Fact]
    public void SupervisionOptions_DefaultValues()
    {
        // Arrange & Act
        var options = new SupervisionOptions();

        // Assert
        Assert.Equal(RestartStrategy.OneForOne, options.RestartStrategy);
        Assert.Equal(3, options.MaxRestarts);
        Assert.Equal(TimeSpan.FromSeconds(60), options.TimeWindow);
        Assert.Equal(TimeSpan.FromSeconds(1), options.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxBackoff);
        Assert.Equal(2.0, options.BackoffMultiplier);
        Assert.True(options.EscalateOnExceeded);
    }

    [Fact]
    public void RestartHistory_RecordRestart_IncrementsCount()
    {
        // Arrange
        var history = new RestartHistory();
        var options = new SupervisionOptions();

        // Act
        history.RecordRestart(DateTimeOffset.UtcNow);
        history.RecordRestart(DateTimeOffset.UtcNow);

        // Assert
        Assert.Equal(2, history.GetRestartsInWindow(options.TimeWindow));
    }

    [Fact]
    public void RestartHistory_CalculateBackoff_ExponentialIncrease()
    {
        // Arrange
        var history = new RestartHistory();
        var options = new SupervisionOptions
        {
            InitialBackoff = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0,
            MaxBackoff = TimeSpan.FromSeconds(30)
        };

        // Act & Assert - First restart
        history.RecordRestart(DateTimeOffset.UtcNow);
        var backoff1 = history.CalculateBackoff(options);
        Assert.Equal(TimeSpan.FromSeconds(1), backoff1);

        // Second restart
        history.RecordRestart(DateTimeOffset.UtcNow);
        var backoff2 = history.CalculateBackoff(options);
        Assert.Equal(TimeSpan.FromSeconds(2), backoff2);

        // Third restart
        history.RecordRestart(DateTimeOffset.UtcNow);
        var backoff3 = history.CalculateBackoff(options);
        Assert.Equal(TimeSpan.FromSeconds(4), backoff3);
    }

    [Fact]
    public void RestartHistory_CalculateBackoff_CapsAtMaxBackoff()
    {
        // Arrange
        var history = new RestartHistory();
        var options = new SupervisionOptions
        {
            InitialBackoff = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0,
            MaxBackoff = TimeSpan.FromSeconds(10)
        };

        // Act - Record many restarts
        for (int i = 0; i < 10; i++)
        {
            history.RecordRestart(DateTimeOffset.UtcNow);
        }

        var backoff = history.CalculateBackoff(options);

        // Assert - Should be capped at max
        Assert.Equal(TimeSpan.FromSeconds(10), backoff);
    }

    [Fact]
    public void RestartHistory_GetRestartsInWindow_ExcludesOld()
    {
        // Arrange
        var history = new RestartHistory();
        var now = DateTimeOffset.UtcNow;

        // Act - Record restarts at different times
        history.RecordRestart(now.AddMinutes(-5)); // Outside 1-minute window
        history.RecordRestart(now.AddSeconds(-30)); // Inside window
        history.RecordRestart(now.AddSeconds(-10)); // Inside window

        // Assert
        var count = history.GetRestartsInWindow(TimeSpan.FromMinutes(1));
        Assert.Equal(2, count);
    }

    [Fact]
    public void RestartHistory_Reset_ClearsHistory()
    {
        // Arrange
        var history = new RestartHistory();
        history.RecordRestart(DateTimeOffset.UtcNow);
        history.RecordRestart(DateTimeOffset.UtcNow);

        // Act
        history.Reset();

        // Assert
        Assert.Equal(0, history.GetRestartsInWindow(TimeSpan.FromMinutes(1)));
        
        // Backoff should be back to initial
        var options = new SupervisionOptions();
        history.RecordRestart(DateTimeOffset.UtcNow);
        var backoff = history.CalculateBackoff(options);
        Assert.Equal(options.InitialBackoff, backoff);
    }

    [Fact]
    public void RestartStrategy_EnumValues()
    {
        // Assert - Verify enum values exist
        Assert.Equal(0, (int)RestartStrategy.OneForOne);
        Assert.Equal(1, (int)RestartStrategy.AllForOne);
        Assert.Equal(2, (int)RestartStrategy.RestForOne);
    }
}
