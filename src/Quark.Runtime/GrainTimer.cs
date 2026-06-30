using Quark.Core.Abstractions.Timers;

namespace Quark.Runtime;

internal sealed class GrainTimer<TState> : IGrainTimer
{
    private readonly Func<TState, CancellationToken, Task> _callback;
    private readonly bool _interleave;
    private readonly Func<Func<ValueTask>, ValueTask> _post;
    private readonly TState _state;
    private readonly Timer _timer;
    private int _pending;
    private volatile bool _disposed;

    internal GrainTimer(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options,
        Func<Func<ValueTask>, ValueTask> postToQueue)
    {
        _callback = callback;
        _state = state;
        _interleave = options.Interleave;
        _post = postToQueue;
        _timer = new Timer(OnFire, null, options.DueTime, options.Period);
    }

    public void Change(TimeSpan dueTime, TimeSpan period)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Change(dueTime, period);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    private void OnFire(object? _)
    {
        if (_disposed)
        {
            return;
        }

        if (!_interleave && Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
        {
            return;
        }

        ValueTask postTask = _post(async () =>
        {
            try
            {
                if (!_disposed)
                {
                    await _callback(_state, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                if (!_interleave)
                {
                    Interlocked.Exchange(ref _pending, 0);
                }
            }
        });

        if (!postTask.IsCompletedSuccessfully)
        {
            Task __ = postTask.AsTask().ContinueWith(
                ContinuationFunction,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void ContinuationFunction(Task _)
    {
        if (!_interleave)
        {
            Interlocked.Exchange(ref _pending, 0);
        }
    }
}
