using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Strongly-typed invokable for <see cref="IRemindable.ReceiveReminder" />.
///     Used by reminder services and the transport path to fire a reminder
///     on a grain without argument boxing or method-ID fan-in.
/// </summary>
public readonly struct ReceiveReminderInvokable : IGrainVoidInvokable
{
    public ReceiveReminderInvokable(string reminderName, TickStatus status)
    {
        ReminderName = reminderName;
        Status = status;
    }

    public string ReminderName { get; }
    public TickStatus Status { get; }
    public uint MethodId => ReminderMethodIds.ReceiveReminder;

    public ValueTask Invoke(IGrainBehavior behavior)
        => new(((IRemindable)behavior).ReceiveReminder(ReminderName, Status));

    public void Serialize(ref CodecWriter writer) { }
}
