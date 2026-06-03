using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerGrain : Grain<WorkerState>, IWorkerGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken); // calls ReadStateAsync

        // Stale-state guard: Status=Processing on load means a prior run didn't finish.
        if (State.Status == WorkerStatus.Processing)
            State = State with { Status = WorkerStatus.Idle };
    }

    public async Task<WorkerStatus> DoWorkAsync()
    {
        State = State with { Status = WorkerStatus.Processing };
        await WriteStateAsync();

        State = State with
        {
            Status = WorkerStatus.Completed,
            RetryCount = State.RetryCount + 1,
            ProcessedAt = DateTimeOffset.UtcNow
        };
        await WriteStateAsync();
        return State.Status;
    }
}
