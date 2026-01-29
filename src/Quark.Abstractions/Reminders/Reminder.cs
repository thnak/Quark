namespace Quark.Abstractions.Reminders;

/// <summary>
///     Represents a persistent reminder for an actor.
///     Unlike volatile timers, reminders survive silo restarts and are stored durably.
/// </summary>
public sealed class Reminder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Reminder"/> class.
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <param name="actorType">The actor type name.</param>
    /// <param name="name">The reminder name (unique per actor).</param>
    /// <param name="dueTime">When the reminder should first fire.</param>
    /// <param name="period">The period for recurring reminders. Null for one-time reminders.</param>
    /// <param name="data">Optional data payload for the reminder.</param>
    public Reminder(
        string actorId,
        string actorType,
        string name,
        DateTimeOffset dueTime,
        TimeSpan? period = null,
        byte[]? data = null)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DueTime = dueTime;
        Period = period;
        Data = data;
        CreatedAt = DateTimeOffset.UtcNow;
        NextFireTime = dueTime;
    }

    /// <summary>
    ///     Gets the actor identifier.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    ///     Gets the actor type name.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    ///     Gets the reminder name (unique per actor).
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets when the reminder should first fire.
    /// </summary>
    public DateTimeOffset DueTime { get; }

    /// <summary>
    ///     Gets the period for recurring reminders. Null for one-time reminders.
    /// </summary>
    public TimeSpan? Period { get; }

    /// <summary>
    ///     Gets optional data payload for the reminder.
    /// </summary>
    public byte[]? Data { get; }

    /// <summary>
    ///     Gets when the reminder was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    ///     Gets or sets the last time this reminder fired.
    /// </summary>
    public DateTimeOffset? LastFiredAt { get; set; }

    /// <summary>
    ///     Gets or sets the next time this reminder should fire.
    /// </summary>
    public DateTimeOffset NextFireTime { get; set; }

    /// <summary>
    ///     Gets the unique reminder ID (combination of actor ID and name).
    /// </summary>
    public string GetId() => $"{ActorId}:{Name}";
}
