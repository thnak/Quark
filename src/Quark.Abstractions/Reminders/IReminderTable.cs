namespace Quark.Abstractions.Reminders;

/// <summary>
///     Interface for storing and retrieving persistent reminders.
///     Reminders are durably stored (e.g., in Redis or SQL) and survive silo restarts.
/// </summary>
public interface IReminderTable
{
    /// <summary>
    ///     Registers a new reminder or updates an existing one.
    /// </summary>
    /// <param name="reminder">The reminder to register.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RegisterAsync(Reminder reminder, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Unregisters a reminder.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="name">The reminder name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UnregisterAsync(string actorId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all reminders for a specific actor.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>List of reminders for the actor.</returns>
    Task<IReadOnlyList<Reminder>> GetRemindersAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets reminders that are due to fire, using consistent hashing to determine
    ///     which reminders this silo is responsible for.
    /// </summary>
    /// <param name="siloId">The current silo identifier.</param>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>List of reminders due to fire that this silo should handle.</returns>
    Task<IReadOnlyList<Reminder>> GetDueRemindersForSiloAsync(
        string siloId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the last fired time and next fire time for a reminder.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="name">The reminder name.</param>
    /// <param name="lastFiredAt">When the reminder last fired.</param>
    /// <param name="nextFireTime">When the reminder should fire next.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UpdateFireTimeAsync(
        string actorId,
        string name,
        DateTimeOffset lastFiredAt,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default);
}
