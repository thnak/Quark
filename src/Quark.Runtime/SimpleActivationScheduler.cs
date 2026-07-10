namespace Quark.Runtime;

/// <summary>
///     Stateless fallback scheduler: dispatches each activation drain as a fire-and-forget
///     <see>
///         <cref>Task.Run</cref>
///     </see>
///     call.  Used by default when no scheduler is registered in DI
///     and by tests that construct <see cref="GrainActivation"/> directly.
/// </summary>
internal sealed class SimpleActivationScheduler : IActivationScheduler
{
    public static readonly SimpleActivationScheduler Instance = new();

    public ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default)
    {
        if (activation.TryMarkScheduled())
            _ = Task.Run(() => RunDrainAsync(activation, cancellationToken), cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task RunDrainAsync(GrainActivation activation, CancellationToken ct)
    {
        if (!activation.TryBeginDrain())
        {
            // Should not happen: _scheduled stays claimed for the whole drain (see
            // GrainActivation.CompleteDrain), so a second Task.Run for this activation is never
            // spawned while one is in flight. Defensive fallback in case that invariant is ever
            // violated — make sure pending work is not stranded.
            if (activation.HasPendingWork)
                await ScheduleAsync(activation, ct).ConfigureAwait(false);
            return;
        }

        (ActivationDrainResult result, bool needsReschedule) =
            await activation.DrainAndCompleteAsync(int.MaxValue, ct).ConfigureAwait(false);
        if (result.HasMoreWork || needsReschedule)
            await ScheduleAsync(activation, ct).ConfigureAwait(false);
    }
}