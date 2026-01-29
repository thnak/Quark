using Quark.Abstractions.Timers;

namespace Quark.Core.Timers;

/// <summary>
///     Implementation of an actor timer using System.Threading.Timer.
///     Timers are lightweight, in-memory, and volatile - they do not survive restarts.
/// </summary>
internal sealed class ActorTimer : IActorTimer
{
    private readonly TimeSpan _dueTime;
    private readonly TimeSpan? _period;
    private readonly Func<Task> _callback;
    private readonly Lock _lock = new();
    private Timer? _timer;
    private volatile bool _isDisposed;
    private volatile bool _isRunning;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorTimer"/> class.
    /// </summary>
    /// <param name="name">The timer name.</param>
    /// <param name="dueTime">When the timer should first fire.</param>
    /// <param name="period">The period for recurring timers. Null for one-time timers.</param>
    /// <param name="callback">The callback to invoke when the timer fires.</param>
    public ActorTimer(string name, TimeSpan dueTime, TimeSpan? period, Func<Task> callback)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _dueTime = dueTime;
        _period = period;
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public bool IsRunning => _isRunning && !_isDisposed;

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (_isRunning)
            {
                Stop();
            }

            _timer = new Timer(
                TimerCallback,
                null,
                _dueTime,
                _period ?? Timeout.InfiniteTimeSpan);

            _isRunning = true;
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _isRunning = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isRunning = false;
            _timer?.Dispose();
            _timer = null;
            _isDisposed = true;
        }
    }

    private void TimerCallback(object? state)
    {
        // Fire and forget - invoke the callback asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                await _callback();
            }
            catch
            {
                // Swallow exceptions - timers should not crash the actor
            }
        });
    }
}
