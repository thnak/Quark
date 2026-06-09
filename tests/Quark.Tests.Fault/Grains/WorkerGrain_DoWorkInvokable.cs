using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Fault.Grains;

internal readonly struct WorkerGrain_DoWorkInvokable : IGrainInvokable<WorkerStatus>
{
    public uint MethodId => 0u;
    public ValueTask<WorkerStatus> Invoke(IGrainBehavior behavior) => new(((IWorkerGrain)behavior).DoWorkAsync());
    public void Serialize(ref CodecWriter writer) { }
    public WorkerStatus DeserializeResult(ref CodecReader reader) => throw new NotSupportedException("Local-only invokable.");
}
