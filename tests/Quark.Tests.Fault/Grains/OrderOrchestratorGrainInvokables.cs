using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Fault.Grains;

internal readonly struct OrderOrchestratorGrain_ProcessInvokable : IGrainInvokable<OrchestratorStatus>
{
    private readonly string[] _workerIds;

    public OrderOrchestratorGrain_ProcessInvokable(string[] workerIds) => _workerIds = workerIds;

    public uint MethodId => 0u;

    public ValueTask<OrchestratorStatus> Invoke(Grain grain)
        => new(((IOrderOrchestratorGrain)grain).ProcessAsync(_workerIds));

    public void Serialize(ref CodecWriter writer) { }
    public OrchestratorStatus DeserializeResult(ref CodecReader reader) => throw new NotSupportedException("Local-only invokable.");
}
