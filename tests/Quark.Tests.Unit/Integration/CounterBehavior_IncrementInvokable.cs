using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.Integration;

internal readonly struct CounterBehavior_IncrementInvokable : IGrainInvokable<long>
{
    public uint MethodId => 0u;
    public ValueTask<long> Invoke(IGrainBehavior behavior) => new(((ICounterGrain)behavior).IncrementAsync());
    public void Serialize(ref CodecWriter writer) { }
    public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
}