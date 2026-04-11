namespace Quark.Core.Abstractions.Lifecycle;

/// <summary>
/// A lifecycle subject that observers can subscribe to.
/// Start/stop are driven sequentially through registered stages.
/// </summary>
public interface ILifecycleSubject
{
    /// <summary>
    /// Subscribes an observer at the specified <paramref name="stage"/>.
    /// </summary>
    /// <param name="observerName">Human-readable name for diagnostics.</param>
    /// <param name="stage">Stage number (lower = earlier start, later stop).</param>
    /// <param name="observer">The observer to subscribe.</param>
    /// <returns>A disposable that unsubscribes when disposed.</returns>
    IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer);
}
