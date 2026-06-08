using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.Integration;

internal readonly struct CounterGrain_IncrementInvokable : IGrainInvokable<long>
{
    public uint MethodId => 0u;
    public ValueTask<long> Invoke(Grain grain) => new(((ICounterGrain)grain).IncrementAsync());
    public void Serialize(ref CodecWriter writer) { }
    public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
}

internal readonly struct CounterGrain_GetValueInvokable : IGrainInvokable<long>
{
    public uint MethodId => 1u;
    public ValueTask<long> Invoke(Grain grain) => new(((ICounterGrain)grain).GetValueAsync());
    public void Serialize(ref CodecWriter writer) { }
    public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
}

internal readonly struct CounterGrain_ResetInvokable : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(Grain grain) => new(((ICounterGrain)grain).ResetAsync());
    public void Serialize(ref CodecWriter writer) { }
}

internal readonly struct CounterGrain_SelfDestructInvokable : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(Grain grain) => new(((ICounterGrain)grain).SelfDestructAsync());
    public void Serialize(ref CodecWriter writer) { }
}
