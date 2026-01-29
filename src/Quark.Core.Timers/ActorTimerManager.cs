using System.Collections.Concurrent;
using Quark.Abstractions.Timers;

namespace Quark.Core.Timers;

/// <summary>
///     Default implementation of actor timer manager.
///     Manages multiple timers for a single actor.
/// </summary>
public sealed class ActorTimerManager : IActorTimerManager
{
    private readonly ConcurrentDictionary<string, ActorTimer> _timers = new();
    private volatile bool _isDisposed;

    /// <inheritdoc />
    public IActorTimer RegisterTimer(string name, TimeSpan dueTime, TimeSpan? period, Func<Task> callback)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Timer name cannot be null or whitespace.", nameof(name));
        }

        var timer = new ActorTimer(name, dueTime, period, callback);

        if (!_timers.TryAdd(name, timer))
        {
            timer.Dispose();
            throw new ArgumentException($"A timer with the name '{name}' already exists.", nameof(name));
        }

        timer.Start();
        return timer;
    }

    /// <inheritdoc />
    public bool UnregisterTimer(string name)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_timers.TryRemove(name, out var timer))
        {
            timer.Dispose();
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public IActorTimer? GetTimer(string name)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return _timers.TryGetValue(name, out var timer) ? timer : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<IActorTimer> GetAllTimers()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return _timers.Values.Cast<IActorTimer>().ToList();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }

        _timers.Clear();
    }
}
