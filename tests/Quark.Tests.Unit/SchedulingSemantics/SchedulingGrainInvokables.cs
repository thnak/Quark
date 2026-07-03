using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.SchedulingSemantics;

internal readonly struct SchedulingGrain_RecordInvokable(int index) : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ISchedulingGrain)behavior).RecordAsync(index));
    public void Serialize(ref CodecWriter writer) => writer.WriteInt32(index);
}

internal readonly struct SchedulingGrain_GetOrderInvokable : IGrainInvokable<int[]>
{
    public uint MethodId => 1u;
    public ValueTask<int[]> Invoke(IGrainBehavior behavior) => new(((ISchedulingGrain)behavior).GetOrderAsync());
    public void Serialize(ref CodecWriter writer) { }
    public int[] DeserializeResult(ref CodecReader reader)
    {
        var result = new int[reader.ReadVarUInt32()];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = reader.ReadInt32();
        }
        return result;
    }
}

internal readonly struct SchedulingGrain_BlockThenRecordInvokable(int index) : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ISchedulingGrain)behavior).BlockThenRecordAsync(index));
    public void Serialize(ref CodecWriter writer) => writer.WriteInt32(index);
}

internal readonly struct SchedulingGrain_NoOpInvokable : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ISchedulingGrain)behavior).NoOpAsync());
    public void Serialize(ref CodecWriter writer) { }
}

internal readonly struct SchedulingGrain_StartTimerInvokable(bool interleave) : IGrainVoidInvokable
{
    public uint MethodId => 4u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ISchedulingGrain)behavior).StartTimerAsync(interleave));
    public void Serialize(ref CodecWriter writer) => writer.WriteByte(interleave ? (byte)1 : (byte)0);
}

internal readonly struct SchedulingGrain_GetTimerFireCountInvokable : IGrainInvokable<int>
{
    public uint MethodId => 5u;
    public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((ISchedulingGrain)behavior).GetTimerFireCountAsync());
    public void Serialize(ref CodecWriter writer) { }
    public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
}

internal readonly struct SchedulingGrain_SelfDestructInvokable : IGrainVoidInvokable
{
    public uint MethodId => 6u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ISchedulingGrain)behavior).SelfDestructAsync());
    public void Serialize(ref CodecWriter writer) { }
}
