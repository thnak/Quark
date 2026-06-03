using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Fault.Grains;

public interface IWorkerGrain : IGrainWithStringKey
{
    Task<WorkerStatus> DoWorkAsync();
}
