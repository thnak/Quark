using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Abstractions.Reminders;

namespace Quark.Core.Reminders;

/// <summary>
///     Background service that polls the reminder table and fires due reminders.
///     Uses consistent hashing to determine which reminders this silo is responsible for.
/// </summary>
public sealed class ReminderTickManager : BackgroundService
{
    private readonly IReminderTable _reminderTable;
    private readonly string _siloId;
    private readonly ILogger<ReminderTickManager> _logger;
    private readonly TimeSpan _tickInterval;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReminderTickManager"/> class.
    /// </summary>
    /// <param name="reminderTable">The reminder table to poll.</param>
    /// <param name="siloId">The current silo identifier.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="tickInterval">How often to poll for due reminders. Defaults to 1 second.</param>
    public ReminderTickManager(
        IReminderTable reminderTable,
        string siloId,
        ILogger<ReminderTickManager> logger,
        TimeSpan? tickInterval = null)
    {
        _reminderTable = reminderTable ?? throw new ArgumentNullException(nameof(reminderTable));
        _siloId = siloId ?? throw new ArgumentNullException(nameof(siloId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tickInterval = tickInterval ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    ///     Event raised when a reminder needs to be fired.
    ///     Subscribers should send a QuarkEnvelope to the target actor.
    /// </summary>
    public event EventHandler<ReminderFiredEventArgs>? ReminderFired;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reminder tick manager started for silo {SiloId}", _siloId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in reminder tick manager");
            }

            await Task.Delay(_tickInterval, stoppingToken);
        }

        _logger.LogInformation("Reminder tick manager stopped for silo {SiloId}", _siloId);
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dueReminders = await _reminderTable.GetDueRemindersForSiloAsync(_siloId, now, cancellationToken);

        foreach (var reminder in dueReminders)
        {
            try
            {
                _logger.LogDebug(
                    "Firing reminder {ReminderName} for actor {ActorId}",
                    reminder.Name,
                    reminder.ActorId);

                // Raise event for subscribers to handle
                ReminderFired?.Invoke(this, new ReminderFiredEventArgs(reminder));

                // Update next fire time
                var nextFireTime = CalculateNextFireTime(reminder, now);
                if (nextFireTime.HasValue)
                {
                    await _reminderTable.UpdateFireTimeAsync(
                        reminder.ActorId,
                        reminder.Name,
                        now,
                        nextFireTime.Value,
                        cancellationToken);
                }
                else
                {
                    // One-time reminder, unregister it
                    await _reminderTable.UnregisterAsync(reminder.ActorId, reminder.Name, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error firing reminder {ReminderName} for actor {ActorId}",
                    reminder.Name,
                    reminder.ActorId);
            }
        }
    }

    private static DateTimeOffset? CalculateNextFireTime(Reminder reminder, DateTimeOffset firedAt)
    {
        if (reminder.Period == null)
        {
            // One-time reminder
            return null;
        }

        return firedAt + reminder.Period.Value;
    }
}

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
