using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerBehavior : IGrainBehavior, IWorkerGrain, IActivationLifecycle
{
    private readonly IPersistentActivationMemory<WorkerState> _memory;

    public WorkerBehavior(IPersistentActivationMemory<WorkerState> memory)
        => _memory = memory;

    private WorkerState S => _memory.Value;

    public async Task OnActivateAsync(CancellationToken ct)
    {
        await _memory.LoadAsync(ct);

        // Stale-state guard: Status=Processing on load means a prior run didn't finish.
        if (S.Status == WorkerStatus.Processing)
            S.Status = WorkerStatus.Idle;
    }

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => _memory.SaveAsync(ct);

    public async Task<WorkerStatus> DoWorkAsync()
    {
        S.Status = WorkerStatus.Processing;
        await _memory.SaveAsync();

        S.Status = WorkerStatus.Completed;
        S.RetryCount++;
        S.ProcessedAt = DateTimeOffset.UtcNow;
        await _memory.SaveAsync();
        return S.Status;
    }
}
