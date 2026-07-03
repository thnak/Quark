using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.FailureSemantics;

internal readonly struct FlakyActivationGrain_PingInvokable : IGrainInvokable<int>
{
    public uint MethodId => 0u;
    public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((IFlakyActivationGrain)behavior).PingAsync());
    public void Serialize(ref CodecWriter writer) { }
    public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
}
