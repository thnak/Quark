using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorBehavior : IGrainBehavior, IOrderOrchestratorGrain, IActivationLifecycle
{
    private readonly IPersistentActivationMemory<OrchestratorState> _memory;
    private readonly IGrainFactory _factory;

    public OrderOrchestratorBehavior(
        IPersistentActivationMemory<OrchestratorState> memory,
        IGrainFactory factory)
    {
        _memory = memory;
        _factory = factory;
    }

    private OrchestratorState S => _memory.Value;

    public Task OnActivateAsync(CancellationToken ct) => _memory.LoadAsync(ct);
    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => _memory.SaveAsync(ct);

    public async Task<OrchestratorStatus> ProcessAsync(string[] workerIds)
    {
        S.WorkerIds = workerIds;
        S.Status = OrchestratorStatus.Processing;
        await _memory.SaveAsync();

        int completionCount = 0;
        bool anyFailed = false;

        foreach (string wid in workerIds)
        {
            bool succeeded = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    IWorkerGrain worker = _factory.GetGrain<IWorkerGrain>(wid);
                    await worker.DoWorkAsync();
                    succeeded = true;
                    break;
                }
                catch (Exception)
                {
                    // 3 total attempts per worker
                }
            }

            if (succeeded) completionCount++;
            else anyFailed = true;
        }

        S.Status = anyFailed ? OrchestratorStatus.Failed : OrchestratorStatus.Completed;
        S.CompletionCount = completionCount;
        await _memory.SaveAsync();
        return S.Status;
    }
}
