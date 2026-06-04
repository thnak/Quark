using Quark.Core.Abstractions.Reminders;

namespace Quark.Reminders.Abstractions;

internal sealed class GrainReminder(string reminderName) : IGrainReminder
{
    public string ReminderName { get; } = reminderName;
    public bool IsValid { get; } = true;
}
