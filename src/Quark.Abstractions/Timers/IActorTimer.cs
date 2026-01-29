namespace Quark.Abstractions.Timers;

/// <summary>
///     Interface for actor timers.
///     Unlike reminders, timers are lightweight, in-memory, and volatile - they do not survive restarts.
/// </summary>
public interface IActorTimer : IDisposable
{
    /// <summary>
    ///     Gets the timer name.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Gets a value indicating whether the timer is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     Starts or restarts the timer.
    /// </summary>
    void Start();

    /// <summary>
    ///     Stops the timer.
    /// </summary>
    void Stop();
}
