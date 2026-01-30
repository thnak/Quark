namespace Quark.Abstractions;

/// <summary>
/// Configuration options for the Dead Letter Queue.
/// </summary>
public sealed class DeadLetterQueueOptions
{
    /// <summary>
    /// Gets or sets whether the Dead Letter Queue is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of messages to retain in the DLQ.
    /// Older messages are removed when this limit is reached (FIFO).
    /// Default is 10000.
    /// </summary>
    public int MaxMessages { get; set; } = 10000;

    /// <summary>
    /// Gets or sets whether to capture exception stack traces.
    /// Disabling this can save memory for high-volume failures.
    /// Default is true.
    /// </summary>
    public bool CaptureStackTraces { get; set; } = true;

    /// <summary>
    /// Gets or sets the global retry policy for failed messages.
    /// If not set, messages go directly to DLQ on failure without retry.
    /// Can be overridden per actor type.
    /// </summary>
    public RetryPolicy? GlobalRetryPolicy { get; set; }

    /// <summary>
    /// Gets or sets per-actor-type DLQ configurations.
    /// These override global settings for specific actor types.
    /// </summary>
    public Dictionary<string, ActorTypeDeadLetterQueueOptions> ActorTypeConfigurations { get; set; } = new();

    /// <summary>
    /// Gets the effective configuration for a given actor type.
    /// Merges actor-specific settings with global defaults.
    /// </summary>
    /// <param name="actorTypeName">The actor type name.</param>
    /// <returns>The effective configuration.</returns>
    public (bool Enabled, int MaxMessages, bool CaptureStackTraces, RetryPolicy? RetryPolicy) GetEffectiveConfiguration(string actorTypeName)
    {
        if (ActorTypeConfigurations.TryGetValue(actorTypeName, out var actorConfig))
        {
            return (
                actorConfig.Enabled ?? Enabled,
                actorConfig.MaxMessages ?? MaxMessages,
                actorConfig.CaptureStackTraces ?? CaptureStackTraces,
                actorConfig.RetryPolicy ?? GlobalRetryPolicy
            );
        }

        return (Enabled, MaxMessages, CaptureStackTraces, GlobalRetryPolicy);
    }
}
