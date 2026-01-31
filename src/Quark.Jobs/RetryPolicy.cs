namespace Quark.Jobs;

/// <summary>
///     Defines the retry policy for a job.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>
    ///     Gets or sets the maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    ///     Gets or sets the initial delay before the first retry.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Gets or sets the backoff multiplier (for exponential backoff).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    ///     Gets or sets the maximum delay between retries.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets the delay for a specific retry attempt.
    /// </summary>
    /// <param name="attemptNumber">The attempt number (1-based).</param>
    /// <returns>The delay before the next retry.</returns>
    public TimeSpan GetDelay(int attemptNumber)
    {
        if (attemptNumber <= 0)
            return TimeSpan.Zero;

        var delay = TimeSpan.FromTicks(
            (long)(InitialDelay.Ticks * Math.Pow(BackoffMultiplier, attemptNumber - 1)));

        return delay > MaxDelay ? MaxDelay : delay;
    }

    /// <summary>
    ///     Creates a default retry policy with exponential backoff.
    /// </summary>
    public static RetryPolicy Default => new()
    {
        MaxRetries = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        BackoffMultiplier = 2.0,
        MaxDelay = TimeSpan.FromMinutes(5)
    };

    /// <summary>
    ///     Creates a retry policy with no retries.
    /// </summary>
    public static RetryPolicy None => new()
    {
        MaxRetries = 0
    };
}
