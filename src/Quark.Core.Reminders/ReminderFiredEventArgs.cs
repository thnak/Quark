using Quark.Abstractions.Reminders;

namespace Quark.Core.Reminders;

/// <summary>
///     Event args for when a reminder fires.
/// </summary>
public sealed class ReminderFiredEventArgs : EventArgs
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ReminderFiredEventArgs"/> class.
    /// </summary>
    /// <param name="reminder">The reminder that fired.</param>
    public ReminderFiredEventArgs(Reminder reminder)
    {
        Reminder = reminder ?? throw new ArgumentNullException(nameof(reminder));
    }

    /// <summary>
    ///     Gets the reminder that fired.
    /// </summary>
    public Reminder Reminder { get; }
}