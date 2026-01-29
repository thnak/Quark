namespace Quark.Abstractions;

/// <summary>
/// Strategy for restarting failed child actors.
/// </summary>
public enum RestartStrategy
{
    /// <summary>
    /// Restart only the failed child actor.
    /// Other siblings continue running.
    /// </summary>
    OneForOne,

    /// <summary>
    /// Restart all child actors when one fails.
    /// Ensures consistent state across siblings.
    /// </summary>
    AllForOne,

    /// <summary>
    /// Restart the failed child and all siblings created after it.
    /// Maintains temporal ordering of actor creation.
    /// </summary>
    RestForOne
}

/// <summary>
/// Configuration for supervision behavior.
/// </summary>
public sealed class SupervisionOptions
{
    /// <summary>
    /// Gets or sets the restart strategy.
    /// Default is OneForOne.
    /// </summary>
    public RestartStrategy RestartStrategy { get; set; } = RestartStrategy.OneForOne;

    /// <summary>
    /// Gets or sets the maximum number of restarts within the time window.
    /// Default is 3.
    /// </summary>
    public int MaxRestarts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the time window for counting restarts.
    /// Default is 60 seconds.
    /// </summary>
    public TimeSpan TimeWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the initial backoff duration before first restart.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum backoff duration.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the backoff multiplier for exponential backoff.
    /// Default is 2.0 (double the delay each time).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets whether to escalate to parent if max restarts exceeded.
    /// Default is true.
    /// </summary>
    public bool EscalateOnExceeded { get; set; } = true;
}

/// <summary>
/// Tracks restart history for backoff calculation.
/// </summary>
public sealed class RestartHistory
{
    private readonly Queue<DateTimeOffset> _restarts = new();
    private int _consecutiveRestarts;
    private DateTimeOffset _lastRestart;

    /// <summary>
    /// Records a restart attempt.
    /// </summary>
    /// <param name="timestamp">The timestamp of the restart.</param>
    public void RecordRestart(DateTimeOffset timestamp)
    {
        _restarts.Enqueue(timestamp);
        _consecutiveRestarts++;
        _lastRestart = timestamp;
    }

    /// <summary>
    /// Calculates the backoff duration based on restart history.
    /// </summary>
    /// <param name="options">The supervision options.</param>
    /// <returns>The backoff duration.</returns>
    public TimeSpan CalculateBackoff(SupervisionOptions options)
    {
        var backoff = options.InitialBackoff;
        
        for (int i = 0; i < _consecutiveRestarts - 1; i++)
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
    /// Gets the number of restarts within the specified time window.
    /// </summary>
    /// <param name="window">The time window.</param>
    /// <returns>The restart count.</returns>
    public int GetRestartsInWindow(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        
        // Remove old restarts
        while (_restarts.Count > 0 && _restarts.Peek() < cutoff)
        {
            _restarts.Dequeue();
        }

        return _restarts.Count;
    }

    /// <summary>
    /// Resets the restart history (e.g., after successful stabilization).
    /// </summary>
    public void Reset()
    {
        _restarts.Clear();
        _consecutiveRestarts = 0;
    }
}
