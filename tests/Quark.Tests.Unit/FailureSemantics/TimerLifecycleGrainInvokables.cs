using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.FailureSemantics;

internal readonly struct TimerLifecycleGrain_StartTimerInvokable(bool timerThrows) : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ITimerLifecycleGrain)behavior).StartTimerAsync(timerThrows));
    public void Serialize(ref CodecWriter writer) => writer.WriteByte(timerThrows ? (byte)1 : (byte)0);
}

internal readonly struct TimerLifecycleGrain_GetFireCountInvokable : IGrainInvokable<int>
{
    public uint MethodId => 1u;
    public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((ITimerLifecycleGrain)behavior).GetFireCountAsync());
    public void Serialize(ref CodecWriter writer) { }
    public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
}

internal readonly struct TimerLifecycleGrain_ThrowInvokable(string message) : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ITimerLifecycleGrain)behavior).ThrowAsync(message));
    public void Serialize(ref CodecWriter writer) => writer.WriteString(message);
}

internal readonly struct TimerLifecycleGrain_SelfDestructInvokable : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ITimerLifecycleGrain)behavior).SelfDestructAsync());
    public void Serialize(ref CodecWriter writer) { }
}
