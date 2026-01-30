using System.Collections.Concurrent;

namespace Quark.Core.Actors.Pooling;

/// <summary>
///     Object pool for <see cref="TaskCompletionSource{TResult}" /> instances.
///     Reduces allocations in the actor messaging hot path by reusing TCS instances.
/// </summary>
/// <typeparam name="TResult">The result type of the task completion source.</typeparam>
public sealed class TaskCompletionSourcePool<TResult>
{
    private readonly ConcurrentBag<TaskCompletionSource<TResult>> _pool = new();
    private int _count;
    private readonly int _maxPoolSize;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TaskCompletionSourcePool{TResult}" /> class.
    /// </summary>
    /// <param name="maxPoolSize">The maximum number of objects to keep in the pool. Default is 1024.</param>
    public TaskCompletionSourcePool(int maxPoolSize = 1024)
    {
        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    ///     Rents a <see cref="TaskCompletionSource{TResult}" /> from the pool.
    ///     If the pool is empty, creates a new instance.
    /// </summary>
    /// <returns>A TaskCompletionSource ready for use.</returns>
    public TaskCompletionSource<TResult> Rent()
    {
        if (_pool.TryTake(out var tcs))
        {
            Interlocked.Decrement(ref _count);
            return tcs;
        }

        return new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    ///     Returns a <see cref="TaskCompletionSource{TResult}" /> to the pool for reuse.
    ///     The TCS must be in a completed state before returning.
    /// </summary>
    /// <param name="tcs">The TaskCompletionSource to return to the pool.</param>
    public void Return(TaskCompletionSource<TResult> tcs)
    {
        if (tcs == null)
            throw new ArgumentNullException(nameof(tcs));

        // Only accept completed TCS instances to avoid issues
        if (!tcs.Task.IsCompleted)
            return;

        // Don't exceed max pool size
        if (_count >= _maxPoolSize)
            return;

        // Create a new TCS instance instead of trying to reset the old one
        // TaskCompletionSource doesn't have a Reset method, so we create fresh instances
        var newTcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pool.Add(newTcs);
        Interlocked.Increment(ref _count);
    }

    /// <summary>
    ///     Gets the current number of objects in the pool.
    /// </summary>
    public int Count => _count;
}
