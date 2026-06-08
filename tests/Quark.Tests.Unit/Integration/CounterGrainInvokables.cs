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

internal readonly struct CounterBehavior_GetValueInvokable : IGrainInvokable<long>
{
    public uint MethodId => 1u;
    public ValueTask<long> Invoke(IGrainBehavior behavior) => new(((ICounterGrain)behavior).GetValueAsync());
    public void Serialize(ref CodecWriter writer) { }
    public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
}

internal readonly struct CounterBehavior_ResetInvokable : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ICounterGrain)behavior).ResetAsync());
    public void Serialize(ref CodecWriter writer) { }
}

internal readonly struct CounterBehavior_SelfDestructInvokable : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ICounterGrain)behavior).SelfDestructAsync());
    public void Serialize(ref CodecWriter writer) { }
}
