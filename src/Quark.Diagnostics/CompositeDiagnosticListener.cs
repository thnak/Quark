using Quark.Diagnostics.Abstractions;

namespace Quark.Diagnostics;

/// <summary>
///     Fans out every diagnostic event to a list of registered <see cref="IQuarkDiagnosticListener" />s.
///     Register via <c>services.AddQuarkDiagnostics&lt;TListener&gt;()</c> — each call appends to the composite.
/// </summary>
public sealed class CompositeDiagnosticListener : IQuarkDiagnosticListener
{
    private readonly IQuarkDiagnosticListener[] _listeners;

    public CompositeDiagnosticListener(IEnumerable<IQuarkDiagnosticListener> listeners)
        => _listeners = listeners.ToArray();

    public void OnGrainActivating(in GrainActivatingEvent e)
    {
        foreach (var l in _listeners) l.OnGrainActivating(in e);
    }

    public void OnGrainActivated(in GrainActivatedEvent e)
    {
        foreach (var l in _listeners) l.OnGrainActivated(in e);
    }

    public void OnGrainDeactivating(in GrainDeactivatingEvent e)
    {
        foreach (var l in _listeners) l.OnGrainDeactivating(in e);
    }

    public void OnGrainDeactivated(in GrainDeactivatedEvent e)
    {
        foreach (var l in _listeners) l.OnGrainDeactivated(in e);
    }

    public void OnInvocationStart(in InvocationStartEvent e)
    {
        foreach (var l in _listeners) l.OnInvocationStart(in e);
    }

    public void OnInvocationEnd(in InvocationEndEvent e)
    {
        foreach (var l in _listeners) l.OnInvocationEnd(in e);
    }

    public void OnMailboxEnqueued(in MailboxEnqueuedEvent e)
    {
        foreach (var l in _listeners) l.OnMailboxEnqueued(in e);
    }

    public void OnMailboxStuck(in MailboxStuckEvent e)
    {
        foreach (var l in _listeners) l.OnMailboxStuck(in e);
    }

    public void OnMailboxStuckResolved(in MailboxStuckResolvedEvent e)
    {
        foreach (var l in _listeners) l.OnMailboxStuckResolved(in e);
    }

    public void OnConnectionAccepted(in ConnectionAcceptedEvent e)
    {
        foreach (var l in _listeners) l.OnConnectionAccepted(in e);
    }

    public void OnConnectionClosed(in ConnectionClosedEvent e)
    {
        foreach (var l in _listeners) l.OnConnectionClosed(in e);
    }

    public void OnMessageDispatched(in MessageDispatchedEvent e)
    {
        foreach (var l in _listeners) l.OnMessageDispatched(in e);
    }

    public void OnObserverRegistered(in ObserverRegisteredEvent e)
    {
        foreach (var l in _listeners) l.OnObserverRegistered(in e);
    }

    public void OnObserverDeregistered(in ObserverDeregisteredEvent e)
    {
        foreach (var l in _listeners) l.OnObserverDeregistered(in e);
    }

    public void OnObserverInvoked(in ObserverInvokedEvent e)
    {
        foreach (var l in _listeners) l.OnObserverInvoked(in e);
    }

    public void OnTimerFired(in TimerFiredEvent e)
    {
        foreach (var l in _listeners) l.OnTimerFired(in e);
    }
}
