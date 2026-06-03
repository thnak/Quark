using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Fault.Grains;

public interface IOrderOrchestratorGrain : IGrainWithStringKey
{
    Task<OrchestratorStatus> ProcessAsync(string[] workerIds);
}
