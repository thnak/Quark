namespace Quark.Abstractions.Reminders;

/// <summary>
///     Interface for actors that can receive reminders.
/// </summary>
public interface IRemindable
{
    /// <summary>
    ///     Called when a reminder fires.
    /// </summary>
    /// <param name="reminderName">The name of the reminder that fired.</param>
    /// <param name="data">Optional data associated with the reminder.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ReceiveReminderAsync(string reminderName, byte[]? data, CancellationToken cancellationToken = default);
}
