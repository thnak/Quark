namespace Quark.Core.Abstractions.Reminders;

/// <summary>Handle returned by <c>RegisterOrUpdateReminderAsync</c>.</summary>
public interface IGrainReminder
{
    string ReminderName { get; }

    /// <summary>
    ///     <c>true</c> if the reminder is still registered in the backing store;
    ///     <c>false</c> if it has been unregistered since this handle was obtained.
    ///     Implementations that do not support staleness detection may always return <c>true</c>.
    /// </summary>
    bool IsValid { get; }
}
