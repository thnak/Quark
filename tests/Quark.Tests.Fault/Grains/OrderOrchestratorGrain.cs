using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorGrain : Grain<OrchestratorState>, IOrderOrchestratorGrain
{
    public async Task<OrchestratorStatus> ProcessAsync(string[] workerIds)
    {
        State = State with { WorkerIds = workerIds, Status = OrchestratorStatus.Processing };
        await WriteStateAsync();

        int completionCount = 0;
        bool anyFailed = false;

        foreach (string wid in workerIds)
        {
            bool succeeded = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    IWorkerGrain worker = GrainFactory.GetGrain<IWorkerGrain>(wid);
                    await worker.DoWorkAsync();
                    succeeded = true;
                    break;
                }
                catch (Exception)
                {
                    // 3 total attempts per worker; silent catch lets the loop continue to next attempt
                }
            }

            if (succeeded) completionCount++;
            else anyFailed = true;
        }

        OrchestratorStatus finalStatus = anyFailed
            ? OrchestratorStatus.Failed
            : OrchestratorStatus.Completed;

        State = State with { Status = finalStatus, CompletionCount = completionCount };
        await WriteStateAsync();
        return finalStatus;
    }
}
