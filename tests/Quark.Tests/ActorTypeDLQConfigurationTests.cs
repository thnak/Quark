using Quark.Abstractions;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for per-actor-type Dead Letter Queue configuration.
/// </summary>
public class ActorTypeDLQConfigurationTests
{
    [Fact]
    public void DeadLetterQueueOptions_GetEffectiveConfiguration_ReturnsGlobalDefaults()
    {
        // Arrange
        var options = new DeadLetterQueueOptions
        {
            Enabled = true,
            MaxMessages = 5000,
            CaptureStackTraces = false
        };

        // Act
        var (enabled, maxMessages, captureStackTraces, retryPolicy) = 
            options.GetEffectiveConfiguration("UnknownActorType");

        // Assert
        Assert.True(enabled);
        Assert.Equal(5000, maxMessages);
        Assert.False(captureStackTraces);
        Assert.Null(retryPolicy);
    }

    [Fact]
    public void DeadLetterQueueOptions_GetEffectiveConfiguration_ReturnsActorSpecificConfig()
    {
        // Arrange
        var actorRetryPolicy = new RetryPolicy { MaxRetries = 5, InitialDelayMs = 200 };
        var options = new DeadLetterQueueOptions
        {
            Enabled = true,
            MaxMessages = 5000,
            CaptureStackTraces = false,
            ActorTypeConfigurations = new Dictionary<string, ActorTypeDeadLetterQueueOptions>
            {
                ["PaymentProcessor"] = new ActorTypeDeadLetterQueueOptions
                {
                    ActorTypeName = "PaymentProcessor",
                    Enabled = false,
                    MaxMessages = 100,
                    CaptureStackTraces = true,
                    RetryPolicy = actorRetryPolicy
                }
            }
        };

        // Act
        var (enabled, maxMessages, captureStackTraces, retryPolicy) = 
            options.GetEffectiveConfiguration("PaymentProcessor");

        // Assert
        Assert.False(enabled); // Actor-specific override
        Assert.Equal(100, maxMessages); // Actor-specific override
        Assert.True(captureStackTraces); // Actor-specific override
        Assert.NotNull(retryPolicy);
        Assert.Same(actorRetryPolicy, retryPolicy); // Actor-specific retry policy
    }

    [Fact]
    public void DeadLetterQueueOptions_GetEffectiveConfiguration_MergesPartialConfig()
    {
        // Arrange
        var options = new DeadLetterQueueOptions
        {
            Enabled = true,
            MaxMessages = 5000,
            CaptureStackTraces = false,
            ActorTypeConfigurations = new Dictionary<string, ActorTypeDeadLetterQueueOptions>
            {
                ["OrderProcessor"] = new ActorTypeDeadLetterQueueOptions
                {
                    ActorTypeName = "OrderProcessor",
                    Enabled = false, // Override only this
                    MaxMessages = null, // Use global
                    CaptureStackTraces = null // Use global
                }
            }
        };

        // Act
        var (enabled, maxMessages, captureStackTraces, retryPolicy) = 
            options.GetEffectiveConfiguration("OrderProcessor");

        // Assert
        Assert.False(enabled); // Actor-specific override
        Assert.Equal(5000, maxMessages); // Global default
        Assert.False(captureStackTraces); // Global default
        Assert.Null(retryPolicy); // Global default
    }

    [Fact]
    public void DeadLetterQueueOptions_GetEffectiveConfiguration_GlobalRetryPolicy()
    {
        // Arrange
        var globalRetryPolicy = new RetryPolicy { MaxRetries = 3, InitialDelayMs = 100 };
        var options = new DeadLetterQueueOptions
        {
            Enabled = true,
            MaxMessages = 5000,
            GlobalRetryPolicy = globalRetryPolicy
        };

        // Act
        var (enabled, maxMessages, captureStackTraces, retryPolicy) = 
            options.GetEffectiveConfiguration("AnyActor");

        // Assert
        Assert.NotNull(retryPolicy);
        Assert.Same(globalRetryPolicy, retryPolicy);
    }

    [Fact]
    public void DeadLetterQueueOptions_GetEffectiveConfiguration_ActorRetryPolicyOverridesGlobal()
    {
        // Arrange
        var globalRetryPolicy = new RetryPolicy { MaxRetries = 3 };
        var actorRetryPolicy = new RetryPolicy { MaxRetries = 10 };
        var options = new DeadLetterQueueOptions
        {
            GlobalRetryPolicy = globalRetryPolicy,
            ActorTypeConfigurations = new Dictionary<string, ActorTypeDeadLetterQueueOptions>
            {
                ["CriticalActor"] = new ActorTypeDeadLetterQueueOptions
                {
                    ActorTypeName = "CriticalActor",
                    RetryPolicy = actorRetryPolicy
                }
            }
        };

        // Act
        var (_, _, _, retryPolicy) = options.GetEffectiveConfiguration("CriticalActor");

        // Assert
        Assert.NotNull(retryPolicy);
        Assert.Same(actorRetryPolicy, retryPolicy);
        Assert.Equal(10, retryPolicy.MaxRetries);
    }

    [Fact]
    public void ActorTypeDeadLetterQueueOptions_RequiresActorTypeName()
    {
        // Arrange & Act & Assert
        var options = new ActorTypeDeadLetterQueueOptions
        {
            ActorTypeName = "TestActor",
            Enabled = true
        };

        Assert.Equal("TestActor", options.ActorTypeName);
    }

    [Fact]
    public void ActorTypeDeadLetterQueueOptions_ThrowsOnNullOrWhitespaceActorTypeName()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ActorTypeDeadLetterQueueOptions
        {
            ActorTypeName = "",
            Enabled = true
        });

        Assert.Throws<ArgumentException>(() => new ActorTypeDeadLetterQueueOptions
        {
            ActorTypeName = "   ",
            Enabled = true
        });
    }
}
