using System.Diagnostics;
using System.Threading.Channels;
using Quark.Diagnostics.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Result of a single drain pass on a <see cref="GrainActivation"/>'s mailbox.
/// </summary>
internal readonly record struct ActivationDrainResult(
    bool HasMoreWork,
    bool IsCompleted,
    int ItemsProcessed);

/// <summary>
///     Stateless fallback scheduler: dispatches each activation drain as a fire-and-forget
///     <see cref="Task.Run"/> call.  Used by default when no scheduler is registered in DI
///     and by tests that construct <see cref="GrainActivation"/> directly.
/// </summary>
internal sealed class SimpleActivationScheduler : IActivationScheduler
{
    public static readonly SimpleActivationScheduler Instance = new();

    public ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default)
    {
        if (activation.TryMarkScheduled())
            _ = Task.Run(() => RunDrainAsync(activation, cancellationToken));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task RunDrainAsync(GrainActivation activation, CancellationToken ct)
    {
        if (!activation.TryBeginDrain())
        {
            // Another drain won the CAS. Ensure pending work will still be processed:
            // TryBeginDrain reset _scheduled to 0, so a new ScheduleAsync from PostAsync
            // will add another entry. Check now in case the race left us as the last waker.
            if (activation.HasPendingWork)
                await ScheduleAsync(activation, ct).ConfigureAwait(false);
            return;
        }

        ActivationDrainResult result = await activation.DrainAsync(int.MaxValue, ct).ConfigureAwait(false);
        bool needsReschedule = activation.CompleteDrain(result);
        if (result.HasMoreWork || needsReschedule)
            await ScheduleAsync(activation, ct).ConfigureAwait(false);
    }
}

/// <summary>
///     Centralized activation scheduler with a configurable ready queue and drain workers.
///     Options come from <see cref="SiloRuntimeOptions"/>; defaults preserve previous behavior
///     (unbounded ready queue, <see cref="Environment.ProcessorCount"/> workers, no drain budget).
///     Registered as a singleton by <see cref="RuntimeServiceCollectionExtensions.AddQuarkRuntime"/>.
/// </summary>
internal sealed class ActivationScheduler : IActivationScheduler
{
    private readonly Channel<GrainActivation> _readyQueue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workers;
    private readonly int _drainBudget;
    private readonly int _queueCapacity;
    private readonly SchedulerOverloadMode _overloadMode;
    private readonly IQuarkDiagnosticListener _diagnostics;

    public ActivationScheduler(SiloRuntimeOptions options, IQuarkDiagnosticListener? diagnostics = null)
    {
        int concurrency = Math.Max(1, options.SchedulerMaxConcurrentActivations);
        _drainBudget = Math.Max(1, options.SchedulerDrainBudget);
        _queueCapacity = options.SchedulerReadyQueueCapacity;
        _overloadMode = options.SchedulerOverloadMode;
        _diagnostics = diagnostics ?? NullDiagnosticListener.Instance;

        _readyQueue = _queueCapacity > 0
            ? Channel.CreateBounded<GrainActivation>(
                new BoundedChannelOptions(_queueCapacity)
                {
                    SingleWriter = false,
                    // BoundedChannelFullMode.Wait so WriteAsync blocks when we're in Wait overload mode.
                    // For RejectWhenFull we use TryWrite manually in ScheduleAsync before calling WriteAsync.
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                })
            : Channel.CreateUnbounded<GrainActivation>(
                new UnboundedChannelOptions { SingleWriter = false, AllowSynchronousContinuations = false });

        _workers = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
            _workers[i] = Task.Run(() => RunWorkerAsync(_cts.Token));
    }

    public async ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default)
    {
        if (!activation.TryMarkScheduled())
            return;

        activation.SetSchedulerEnqueueTime();
        _diagnostics.OnSchedulerActivationScheduled(new SchedulerActivationScheduledEvent(activation.GrainId));

        if (_queueCapacity > 0 && _overloadMode == SchedulerOverloadMode.RejectWhenFull)
        {
            if (!_readyQueue.Writer.TryWrite(activation))
            {
                activation.AbortSchedule();
                QuarkInstruments.SchedulerOverloadRejections.Add(1);
                _diagnostics.OnSchedulerOverloadRejected(new SchedulerOverloadRejectedEvent(_queueCapacity));
                throw new SchedulerOverloadException(_queueCapacity);
            }
        }
        else
        {
            await _readyQueue.Writer.WriteAsync(activation, cancellationToken).ConfigureAwait(false);
        }

        QuarkInstruments.SchedulerReadyQueueDepth.Add(1);
        _diagnostics.OnSchedulerReadyQueueDepthChanged(
            new SchedulerReadyQueueDepthChangedEvent(_readyQueue.Reader.Count, 1));
    }

    public async ValueTask DisposeAsync()
    {
        _readyQueue.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch
        {
            // Worker cancellation is expected.
        }
        _cts.Dispose();
    }

    private async Task RunWorkerAsync(CancellationToken ct)
    {
        await foreach (GrainActivation activation in _readyQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            QuarkInstruments.SchedulerReadyQueueDepth.Add(-1);
            _diagnostics.OnSchedulerReadyQueueDepthChanged(
                new SchedulerReadyQueueDepthChangedEvent(_readyQueue.Reader.Count, -1));

            if (!activation.TryBeginDrain())
            {
                // Another drain is running for this activation. The running drain's CompleteDrain
                // will reschedule if more work remains — no action needed here.
                continue;
            }

            long enqueuedAt = activation.TakeSchedulerEnqueueTime();
            double waitMs = enqueuedAt > 0 ? Stopwatch.GetElapsedTime(enqueuedAt).TotalMilliseconds : 0;
            QuarkInstruments.SchedulerActivationWaitDuration.Record(waitMs);
            _diagnostics.OnSchedulerActivationWaited(new SchedulerActivationWaitedEvent(activation.GrainId, waitMs));

            _diagnostics.OnSchedulerDrainStarted(new SchedulerDrainStartedEvent(activation.GrainId));
            QuarkInstruments.SchedulerActiveDrains.Add(1);

            long drainStart = Stopwatch.GetTimestamp();
            ActivationDrainResult result;
            try
            {
                result = await activation.DrainAsync(_drainBudget, ct).ConfigureAwait(false);
            }
            finally
            {
                QuarkInstruments.SchedulerActiveDrains.Add(-1);
            }

            double drainMs = Stopwatch.GetElapsedTime(drainStart).TotalMilliseconds;
            QuarkInstruments.SchedulerDrainDuration.Record(drainMs);
            QuarkInstruments.SchedulerDrainItems.Add(result.ItemsProcessed);
            _diagnostics.OnSchedulerDrainCompleted(
                new SchedulerDrainCompletedEvent(activation.GrainId, result.ItemsProcessed, drainMs));

            // Fairness yield: drain hit budget with work still pending.
            if (result.HasMoreWork && result.ItemsProcessed >= _drainBudget)
            {
                QuarkInstruments.SchedulerDrainYields.Add(1);
                _diagnostics.OnSchedulerDrainYielded(
                    new SchedulerDrainYieldedEvent(activation.GrainId, result.ItemsProcessed));
            }

            bool needsReschedule = activation.CompleteDrain(result);

            if (!result.IsCompleted && (result.HasMoreWork || needsReschedule))
            {
                if (activation.TryMarkScheduled())
                {
                    activation.SetSchedulerEnqueueTime();
                    _readyQueue.Writer.TryWrite(activation);
                    QuarkInstruments.SchedulerReadyQueueDepth.Add(1);
                    _diagnostics.OnSchedulerReadyQueueDepthChanged(
                        new SchedulerReadyQueueDepthChangedEvent(_readyQueue.Reader.Count, 1));
                }
            }
        }
    }
}
