namespace Quark.Abstractions;

/// <summary>
///     Tracks restart history for backoff calculation.
/// </summary>
public sealed class RestartHistory
{
    private readonly Queue<DateTimeOffset> _restarts = new();
    private int _consecutiveRestarts;
    private DateTimeOffset _lastRestart;

    /// <summary>
    ///     Records a restart attempt.
    /// </summary>
    /// <param name="timestamp">The timestamp of the restart.</param>
    public void RecordRestart(DateTimeOffset timestamp)
    {
        _restarts.Enqueue(timestamp);
        _consecutiveRestarts++;
        _lastRestart = timestamp;
    }

    /// <summary>
    ///     Calculates the backoff duration based on restart history.
    /// </summary>
    /// <param name="options">The supervision options.</param>
    /// <returns>The backoff duration.</returns>
    public TimeSpan CalculateBackoff(SupervisionOptions options)
    {
        var backoff = options.InitialBackoff;

        for (var i = 0; i < _consecutiveRestarts - 1; i++)
        {
            backoff = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * options.BackoffMultiplier);
            if (backoff > options.MaxBackoff)
            {
                backoff = options.MaxBackoff;
                break;
            }
        }

        return backoff;
    }

    /// <summary>
    ///     Gets the number of restarts within the specified time window.
    /// </summary>
    /// <param name="window">The time window.</param>
    /// <returns>The restart count.</returns>
    public int GetRestartsInWindow(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;

        // Remove old restarts
        while (_restarts.Count > 0 && _restarts.Peek() < cutoff) _restarts.Dequeue();

        return _restarts.Count;
    }

    /// <summary>
    ///     Resets the restart history (e.g., after successful stabilization).
    /// </summary>
    public void Reset()
    {
        _restarts.Clear();
        _consecutiveRestarts = 0;
    }
}