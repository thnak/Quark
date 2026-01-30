using Quark.Abstractions;
using Quark.Core.Actors;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for retry policy and exponential backoff functionality.
/// </summary>
public class RetryPolicyTests
{
    [Fact]
    public void RetryPolicy_CalculateDelay_ReturnsInitialDelayForFirstAttempt()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            Enabled = true,
            InitialDelayMs = 100,
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act
        var delay = policy.CalculateDelay(1);

        // Assert
        Assert.Equal(100, delay);
    }

    [Fact]
    public void RetryPolicy_CalculateDelay_AppliesExponentialBackoff()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            Enabled = true,
            InitialDelayMs = 100,
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act
        var delay1 = policy.CalculateDelay(1);
        var delay2 = policy.CalculateDelay(2);
        var delay3 = policy.CalculateDelay(3);

        // Assert
        Assert.Equal(100, delay1);    // 100 * 2^0
        Assert.Equal(200, delay2);    // 100 * 2^1
        Assert.Equal(400, delay3);    // 100 * 2^2
    }

    [Fact]
    public void RetryPolicy_CalculateDelay_RespectsMaxDelay()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            Enabled = true,
            InitialDelayMs = 100,
            MaxDelayMs = 300,
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act
        var delay3 = policy.CalculateDelay(3); // Would be 400, capped at 300
        var delay4 = policy.CalculateDelay(4); // Would be 800, capped at 300

        // Assert
        Assert.Equal(300, delay3);
        Assert.Equal(300, delay4);
    }

    [Fact]
    public void RetryPolicy_CalculateDelay_WithJitter_ReturnsVariedDelays()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            Enabled = true,
            InitialDelayMs = 100,
            BackoffMultiplier = 1.0, // No backoff to isolate jitter
            UseJitter = true
        };

        // Act - run multiple times to check jitter varies
        var delays = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            delays.Add(policy.CalculateDelay(1));
        }

        // Assert - jitter should produce different values (with very high probability)
        var uniqueDelays = delays.Distinct().Count();
        Assert.True(uniqueDelays > 1, "Jitter should produce varied delays");
        
        // All delays should be within the jitter range (50% to 150% of base, which is 50 to 150 for base=100)
        Assert.All(delays, d => Assert.InRange(d, 50, 150));
    }

    [Fact]
    public void RetryPolicy_CalculateDelay_ThrowsOnInvalidAttemptNumber()
    {
        // Arrange
        var policy = new RetryPolicy();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => policy.CalculateDelay(0));
        Assert.Throws<ArgumentException>(() => policy.CalculateDelay(-1));
    }

    [Fact]
    public async Task RetryHandler_ExecuteWithRetryAsync_SucceedsOnFirstTry()
    {
        // Arrange
        var policy = new RetryPolicy { Enabled = true, MaxRetries = 3 };
        var handler = new RetryHandler(policy);
        var executionCount = 0;

        // Act
        var result = await handler.ExecuteWithRetryAsync(async () =>
        {
            executionCount++;
            await Task.CompletedTask;
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.RetryCount);
        Assert.Null(result.LastException);
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task RetryHandler_ExecuteWithRetryAsync_RetriesUntilSuccess()
    {
        // Arrange
        var policy = new RetryPolicy 
        { 
            Enabled = true, 
            MaxRetries = 3,
            InitialDelayMs = 10,
            UseJitter = false
        };
        var handler = new RetryHandler(policy);
        var executionCount = 0;

        // Act
        var result = await handler.ExecuteWithRetryAsync(async () =>
        {
            executionCount++;
            if (executionCount < 3)
                throw new InvalidOperationException($"Attempt {executionCount}");
            await Task.CompletedTask;
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.RetryCount); // Succeeded on retry #2
        Assert.Null(result.LastException);
        Assert.Equal(3, executionCount); // Initial + 2 retries
    }

    [Fact]
    public async Task RetryHandler_ExecuteWithRetryAsync_ExhaustsRetries()
    {
        // Arrange
        var policy = new RetryPolicy 
        { 
            Enabled = true, 
            MaxRetries = 2,
            InitialDelayMs = 10,
            UseJitter = false
        };
        var handler = new RetryHandler(policy);
        var executionCount = 0;

        // Act
        var result = await handler.ExecuteWithRetryAsync(async () =>
        {
            executionCount++;
            await Task.CompletedTask;
            throw new InvalidOperationException($"Attempt {executionCount}");
        });

        // Assert
        Assert.False(result.Success);
        Assert.Equal(2, result.RetryCount); // All retries exhausted
        Assert.NotNull(result.LastException);
        Assert.Equal(3, executionCount); // Initial + 2 retries
    }

    [Fact]
    public async Task RetryHandler_ExecuteWithRetryAsync_DisabledPolicy_NoRetry()
    {
        // Arrange
        var policy = new RetryPolicy { Enabled = false, MaxRetries = 3 };
        var handler = new RetryHandler(policy);
        var executionCount = 0;

        // Act
        var result = await handler.ExecuteWithRetryAsync(async () =>
        {
            executionCount++;
            await Task.CompletedTask;
            throw new InvalidOperationException("Always fails");
        });

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.RetryCount);
        Assert.NotNull(result.LastException);
        Assert.Equal(1, executionCount); // Only initial attempt, no retries
    }
}
