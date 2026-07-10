using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance.Shared;

internal readonly struct WorkGrainBehavior_DoWorkInvokable(int microseconds) : IGrainInvokable<long>
{
    public uint MethodId => 0u;
    public ValueTask<long> Invoke(IGrainBehavior behavior) => ((IWorkGrain)behavior).DoWorkAsync(microseconds);
    public void Serialize(ref CodecWriter writer) => writer.WriteInt32(microseconds);

    // Local-only invocation never round-trips through the wire codec — never actually called.
    public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
}
