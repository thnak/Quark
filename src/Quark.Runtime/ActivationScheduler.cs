using System.Threading.Channels;

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
///     Centralized activation scheduler with a global ready queue and
///     <see cref="Environment.ProcessorCount"/> drain workers.
///     Registered as a singleton by <see cref="RuntimeServiceCollectionExtensions.AddQuarkRuntime"/>.
/// </summary>
internal sealed class ActivationScheduler : IActivationScheduler
{
    private readonly Channel<GrainActivation> _readyQueue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workers;

    public ActivationScheduler()
    {
        _readyQueue = Channel.CreateUnbounded<GrainActivation>(
            new UnboundedChannelOptions { SingleWriter = false, AllowSynchronousContinuations = false });

        int count = Math.Max(1, Environment.ProcessorCount);
        _workers = new Task[count];
        for (int i = 0; i < count; i++)
            _workers[i] = Task.Run(() => RunWorkerAsync(_cts.Token));
    }

    public ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default)
    {
        if (activation.TryMarkScheduled())
            _readyQueue.Writer.TryWrite(activation);
        return ValueTask.CompletedTask;
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
            if (!activation.TryBeginDrain())
            {
                // Another drain is already running for this activation. It will either
                // process pending work itself or HasPendingWork will trigger a reschedule.
                if (activation.HasPendingWork)
                    activation.TryMarkScheduled();  // may fail — that's fine, existing entry covers it
                continue;
            }

            ActivationDrainResult result = await activation.DrainAsync(int.MaxValue, ct).ConfigureAwait(false);
            bool needsReschedule = activation.CompleteDrain(result);
            if (result.HasMoreWork || needsReschedule)
            {
                if (activation.TryMarkScheduled())
                    _readyQueue.Writer.TryWrite(activation);
            }
        }
    }
}
