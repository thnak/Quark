using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.Integration;

internal readonly struct CounterBehavior_GetValueInvokable : IGrainInvokable<long>
{
    public uint MethodId => 1u;
    public ValueTask<long> Invoke(IGrainBehavior behavior) => new(((ICounterGrain)behavior).GetValueAsync());
    public void Serialize(ref CodecWriter writer) { }
    public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
}