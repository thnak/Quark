namespace Quark.Abstractions;

/// <summary>
/// Defines retry behavior for failed actor messages before they are sent to the Dead Letter Queue.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>
    /// Gets or sets whether retry is enabled.
    /// Default is false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts before sending to DLQ.
    /// Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the initial retry delay in milliseconds.
    /// Default is 100ms.
    /// </summary>
    public int InitialDelayMs { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum retry delay in milliseconds.
    /// Default is 30000ms (30 seconds).
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;

    /// <summary>
    /// Gets or sets the exponential backoff multiplier.
    /// Each retry delay is multiplied by this value.
    /// Default is 2.0 (doubles each time).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets whether to add jitter to retry delays to prevent thundering herd.
    /// Default is true.
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Calculates the retry delay for a given attempt number.
    /// </summary>
    /// <param name="attemptNumber">The retry attempt number (1-based).</param>
    /// <returns>The delay in milliseconds.</returns>
    public int CalculateDelay(int attemptNumber)
    {
        if (attemptNumber <= 0)
            throw new ArgumentException("Attempt number must be positive.", nameof(attemptNumber));

        // Calculate exponential backoff: InitialDelay * (BackoffMultiplier ^ (attempt - 1))
        var delay = InitialDelayMs * Math.Pow(BackoffMultiplier, attemptNumber - 1);
        delay = Math.Min(delay, MaxDelayMs);

        // Add jitter if enabled (randomize between 50% and 100% of calculated delay)
        if (UseJitter)
        {
            var random = Random.Shared;
            var jitterRange = delay * 0.5; // 50% jitter range
            delay = delay - jitterRange + (random.NextDouble() * jitterRange * 2);
        }

        return (int)Math.Ceiling(delay);
    }
}
