namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Implemented by grains that receive durable reminder callbacks.
///     Orleans-compatible drop-in.
/// </summary>
public interface IRemindable
{
    Task ReceiveReminder(string reminderName, TickStatus status);
}
