using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Represents a single live grain activation on this silo.
///     Owns a sequential <see cref="Channel{T}" /> that ensures grain methods are
///     processed one at a time (single-threaded turn-based execution model).
/// </summary>
public sealed class GrainActivation : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<GrainActivation> _logger;
    private readonly Task _processingLoop;
    private readonly bool _isReentrant;
    private Func<Task>? _onDeactivated;

    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    internal GrainActivation(Grain grain, GrainContext context, ILogger<GrainActivation> logger)
    {
        _logger = logger;
        Grain = grain;
        Context = context;
        _isReentrant = grain.GetType().IsDefined(typeof(ReentrantAttribute), inherit: true);
        _processingLoop = RunLoopAsync(_cts.Token);
        context.SetScheduler(PostAsync);
        context.SetDeactivationCallback(ScheduleDeactivation);
    }

    /// <summary>
    ///     Registers a callback invoked after the grain's deactivation sequence completes and
    ///     the processing loop exits.  Used by <see cref="LocalGrainCallInvoker" /> to remove
    ///     the activation from the <see cref="GrainActivationTable" />.
    /// </summary>
    internal void SetOnDeactivated(Func<Task> onDeactivated)
    {
        _onDeactivated = onDeactivated;
    }

    /// <summary>The grain instance.</summary>
    public Grain Grain { get; }

    /// <summary>The activation context (identity + lifecycle).</summary>
    public GrainContext Context { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Context.ActivationStatus is GrainActivationStatus.Active or GrainActivationStatus.Activating)
        {
            // Run OnDeactivateAsync on the grain's scheduler before tearing down the loop.
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await PostAsync(async () =>
                {
                    try
                    {
                        await Context.DeactivateAsync(Grain, DeactivationReason.ShuttingDown)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        _queue.Writer.TryComplete();
                        done.TrySetResult();
                    }
                }).ConfigureAwait(false);
                await done.Task.ConfigureAwait(false);
            }
            catch
            {
                done.TrySetResult();
            }
        }

        await _cts.CancelAsync();
        _queue.Writer.TryComplete();
        try
        {
            await _processingLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    ///     Called by <see cref="GrainContext.Deactivate" /> (via the deactivation callback).
    ///     Posts the full deactivation sequence as the next work item on the grain's scheduler
    ///     so it runs AFTER the current grain turn completes.  A continuation fires after the
    ///     processing loop exits to invoke <see cref="_onDeactivated" /> (table cleanup).
    /// </summary>
    private void ScheduleDeactivation(DeactivationReason reason)
    {
        _ = PostAsync(() => RunDeactivationAsync(reason));

        _ = _processingLoop.ContinueWith(
            _ => _onDeactivated?.Invoke() ?? Task.CompletedTask,
            TaskScheduler.Default).Unwrap();
    }

    private async Task RunDeactivationAsync(DeactivationReason reason)
    {
        await Context.DeactivateAsync(Grain, reason).ConfigureAwait(false);
        // Signal the processing loop to stop accepting new work after this item.
        _queue.Writer.TryComplete();
    }

    /// <summary>
    ///     Posts a unit of work to this grain's sequential scheduler.
    ///     For non-reentrant grains, the work item will be executed after all previously posted items complete.
    ///     For reentrant grains, the work item executes immediately without queueing.
    /// </summary>
    public async ValueTask PostAsync(Func<Task> workItem)
    {
        if (_isReentrant)
        {
            _cts.Token.ThrowIfCancellationRequested();
            await workItem().ConfigureAwait(false);
            return;
        }

        // Capture the caller's execution context so that AsyncLocal values (e.g. transaction IDs)
        // flow into the grain's execution turn even though it runs on a different thread via the channel.
        var ctx = ExecutionContext.Capture();
        Func<Task> dispatched = ctx is null
            ? workItem
            : () =>
            {
                Task? task = null;
                ExecutionContext.Run(ctx, _ => task = workItem(), null);
                return task!;
            };
        await _queue.Writer.WriteAsync(dispatched, _cts.Token).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await foreach (Func<Task> work in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error executing grain method on {GrainId}", Context.GrainId);
            }
        }
    }
}
