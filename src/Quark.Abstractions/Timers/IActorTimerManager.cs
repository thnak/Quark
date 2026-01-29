namespace Quark.Abstractions.Timers;

/// <summary>
///     Interface for managing actor timers.
/// </summary>
public interface IActorTimerManager : IDisposable
{
    /// <summary>
    ///     Registers a new timer for the actor.
    /// </summary>
    /// <param name="name">The timer name (must be unique per actor).</param>
    /// <param name="dueTime">When the timer should first fire.</param>
    /// <param name="period">The period for recurring timers. Null for one-time timers.</param>
    /// <param name="callback">The callback to invoke when the timer fires.</param>
    /// <returns>The registered timer.</returns>
    /// <exception cref="ArgumentException">Thrown when a timer with the same name already exists.</exception>
    IActorTimer RegisterTimer(string name, TimeSpan dueTime, TimeSpan? period, Func<Task> callback);

    /// <summary>
    ///     Unregisters a timer.
    /// </summary>
    /// <param name="name">The timer name.</param>
    /// <returns>True if the timer was found and unregistered; otherwise, false.</returns>
    bool UnregisterTimer(string name);

    /// <summary>
    ///     Gets a timer by name.
    /// </summary>
    /// <param name="name">The timer name.</param>
    /// <returns>The timer, or null if not found.</returns>
    IActorTimer? GetTimer(string name);

    /// <summary>
    ///     Gets all registered timers.
    /// </summary>
    /// <returns>A read-only list of all timers.</returns>
    IReadOnlyList<IActorTimer> GetAllTimers();
}
