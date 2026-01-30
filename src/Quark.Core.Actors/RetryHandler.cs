using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
/// Provides retry functionality for failed actor messages with exponential backoff.
/// </summary>
public sealed class RetryHandler
{
    private readonly RetryPolicy _retryPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryHandler"/> class.
    /// </summary>
    /// <param name="retryPolicy">The retry policy to use.</param>
    public RetryHandler(RetryPolicy retryPolicy)
    {
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
    }

    /// <summary>
    /// Executes an action with retry logic according to the configured policy.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the action succeeded (possibly after retries), false if all retries exhausted.</returns>
    public async Task<(bool Success, int RetryCount, Exception? LastException)> ExecuteWithRetryAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        if (!_retryPolicy.Enabled)
        {
            // Retry disabled, just execute once
            try
            {
                await action();
                return (true, 0, null);
            }
            catch (Exception ex)
            {
                return (false, 0, ex);
            }
        }

        Exception? lastException = null;
        int attemptNumber = 0;

        // Initial attempt (not counted as a retry)
        try
        {
            await action();
            return (true, 0, null);
        }
        catch (Exception ex)
        {
            lastException = ex;
        }

        // Retry attempts
        for (int retry = 1; retry <= _retryPolicy.MaxRetries; retry++)
        {
            if (cancellationToken.IsCancellationRequested)
                return (false, retry - 1, lastException);

            attemptNumber = retry;

            // Calculate delay with exponential backoff
            var delayMs = _retryPolicy.CalculateDelay(retry);
            await Task.Delay(delayMs, cancellationToken);

            try
            {
                await action();
                return (true, retry, null);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        // All retries exhausted
        return (false, attemptNumber, lastException);
    }
}
