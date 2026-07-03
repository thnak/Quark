using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.FailureSemantics;

internal readonly struct FailureGrain_SetInvokable(int value) : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IFailureGrain)behavior).SetAsync(value));
    public void Serialize(ref CodecWriter writer) => writer.WriteInt32(value);
}

internal readonly struct FailureGrain_GetInvokable : IGrainInvokable<int>
{
    public uint MethodId => 1u;
    public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((IFailureGrain)behavior).GetAsync());
    public void Serialize(ref CodecWriter writer) { }
    public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
}

internal readonly struct FailureGrain_ThrowInvokable(string message) : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IFailureGrain)behavior).ThrowAsync(message));
    public void Serialize(ref CodecWriter writer) => writer.WriteString(message);
}

internal readonly struct FailureGrain_SetThenThrowInvokable(int value) : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IFailureGrain)behavior).SetThenThrowAsync(value));
    public void Serialize(ref CodecWriter writer) => writer.WriteInt32(value);
}
