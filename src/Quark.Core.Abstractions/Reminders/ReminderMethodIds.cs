namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Well-known reserved method ID for <see cref="IRemindable.ReceiveReminder" />.
///     <see cref="Quark.Runtime.LocalGrainCallInvoker" /> dispatches this ID
///     directly without going through the grain's <c>IGrainMethodInvoker</c>.
/// </summary>
public static class ReminderMethodIds
{
    public const uint ReceiveReminder = 0xFFFF_FF00u;
}
