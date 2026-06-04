namespace Quark.Reminders.Abstractions;

/// <summary>Configuration for the reminder polling service.</summary>
public sealed class ReminderOptions
{
    /// <summary>How often the service checks for due reminders. Default: 1 second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
