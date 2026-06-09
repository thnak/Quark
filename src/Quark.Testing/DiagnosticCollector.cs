using System.Collections.Concurrent;
using Quark.Diagnostics.Abstractions;

namespace Quark.Testing;

/// <summary>
///     Thread-safe <see cref="IQuarkDiagnosticListener" /> that records every event for
///     assertion in integration tests.
/// </summary>
public sealed class DiagnosticCollector : IQuarkDiagnosticListener
{
    private readonly ConcurrentQueue<GrainActivatingEvent> _grainActivating = new();
    private readonly ConcurrentQueue<GrainActivatedEvent> _grainActivated = new();
    private readonly ConcurrentQueue<GrainDeactivatingEvent> _grainDeactivating = new();
    private readonly ConcurrentQueue<GrainDeactivatedEvent> _grainDeactivated = new();
    private readonly ConcurrentQueue<InvocationStartEvent> _invocationStart = new();
    private readonly ConcurrentQueue<InvocationEndEvent> _invocationEnd = new();
    private readonly ConcurrentQueue<MailboxEnqueuedEvent> _mailboxEnqueued = new();
    private readonly ConcurrentQueue<MailboxStuckEvent> _mailboxStuck = new();
    private readonly ConcurrentQueue<MailboxStuckResolvedEvent> _mailboxStuckResolved = new();
    private readonly ConcurrentQueue<ConnectionAcceptedEvent> _connectionAccepted = new();
    private readonly ConcurrentQueue<ConnectionClosedEvent> _connectionClosed = new();
    private readonly ConcurrentQueue<MessageDispatchedEvent> _messageDispatched = new();
    private readonly ConcurrentQueue<ObserverRegisteredEvent> _observerRegistered = new();
    private readonly ConcurrentQueue<ObserverDeregisteredEvent> _observerDeregistered = new();
    private readonly ConcurrentQueue<ObserverInvokedEvent> _observerInvoked = new();

    public IReadOnlyCollection<GrainActivatingEvent> GrainActivating => _grainActivating;
    public IReadOnlyCollection<GrainActivatedEvent> GrainActivated => _grainActivated;
    public IReadOnlyCollection<GrainDeactivatingEvent> GrainDeactivating => _grainDeactivating;
    public IReadOnlyCollection<GrainDeactivatedEvent> GrainDeactivated => _grainDeactivated;
    public IReadOnlyCollection<InvocationStartEvent> InvocationStart => _invocationStart;
    public IReadOnlyCollection<InvocationEndEvent> InvocationEnd => _invocationEnd;
    public IReadOnlyCollection<MailboxEnqueuedEvent> MailboxEnqueued => _mailboxEnqueued;
    public IReadOnlyCollection<MailboxStuckEvent> MailboxStuck => _mailboxStuck;
    public IReadOnlyCollection<MailboxStuckResolvedEvent> MailboxStuckResolved => _mailboxStuckResolved;
    public IReadOnlyCollection<ConnectionAcceptedEvent> ConnectionAccepted => _connectionAccepted;
    public IReadOnlyCollection<ConnectionClosedEvent> ConnectionClosed => _connectionClosed;
    public IReadOnlyCollection<MessageDispatchedEvent> MessageDispatched => _messageDispatched;
    public IReadOnlyCollection<ObserverRegisteredEvent> ObserverRegistered => _observerRegistered;
    public IReadOnlyCollection<ObserverDeregisteredEvent> ObserverDeregistered => _observerDeregistered;
    public IReadOnlyCollection<ObserverInvokedEvent> ObserverInvoked => _observerInvoked;

    void IQuarkDiagnosticListener.OnGrainActivating(in GrainActivatingEvent e) => _grainActivating.Enqueue(e);
    void IQuarkDiagnosticListener.OnGrainActivated(in GrainActivatedEvent e) => _grainActivated.Enqueue(e);
    void IQuarkDiagnosticListener.OnGrainDeactivating(in GrainDeactivatingEvent e) => _grainDeactivating.Enqueue(e);
    void IQuarkDiagnosticListener.OnGrainDeactivated(in GrainDeactivatedEvent e) => _grainDeactivated.Enqueue(e);
    void IQuarkDiagnosticListener.OnInvocationStart(in InvocationStartEvent e) => _invocationStart.Enqueue(e);
    void IQuarkDiagnosticListener.OnInvocationEnd(in InvocationEndEvent e) => _invocationEnd.Enqueue(e);
    void IQuarkDiagnosticListener.OnMailboxEnqueued(in MailboxEnqueuedEvent e) => _mailboxEnqueued.Enqueue(e);
    void IQuarkDiagnosticListener.OnMailboxStuck(in MailboxStuckEvent e) => _mailboxStuck.Enqueue(e);
    void IQuarkDiagnosticListener.OnMailboxStuckResolved(in MailboxStuckResolvedEvent e) => _mailboxStuckResolved.Enqueue(e);
    void IQuarkDiagnosticListener.OnConnectionAccepted(in ConnectionAcceptedEvent e) => _connectionAccepted.Enqueue(e);
    void IQuarkDiagnosticListener.OnConnectionClosed(in ConnectionClosedEvent e) => _connectionClosed.Enqueue(e);
    void IQuarkDiagnosticListener.OnMessageDispatched(in MessageDispatchedEvent e) => _messageDispatched.Enqueue(e);
    void IQuarkDiagnosticListener.OnObserverRegistered(in ObserverRegisteredEvent e) => _observerRegistered.Enqueue(e);
    void IQuarkDiagnosticListener.OnObserverDeregistered(in ObserverDeregisteredEvent e) => _observerDeregistered.Enqueue(e);
    void IQuarkDiagnosticListener.OnObserverInvoked(in ObserverInvokedEvent e) => _observerInvoked.Enqueue(e);

    /// <summary>Clears all collected events.</summary>
    public void Reset()
    {
        _grainActivating.Clear();
        _grainActivated.Clear();
        _grainDeactivating.Clear();
        _grainDeactivated.Clear();
        _invocationStart.Clear();
        _invocationEnd.Clear();
        _mailboxEnqueued.Clear();
        _mailboxStuck.Clear();
        _mailboxStuckResolved.Clear();
        _connectionAccepted.Clear();
        _connectionClosed.Clear();
        _messageDispatched.Clear();
        _observerRegistered.Clear();
        _observerDeregistered.Clear();
        _observerInvoked.Clear();
    }

    /// <summary>
    ///     Waits until at least <paramref name="count" /> invocation-end events have been recorded,
    ///     or throws after <paramref name="timeout" />.
    /// </summary>
    public async Task WaitForInvocationsAsync(int count, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (_invocationEnd.Count < count)
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Waits until at least one grain with the given type name has been activated,
    ///     or throws after <paramref name="timeout" />.
    /// </summary>
    public async Task WaitForActivationAsync(string grainTypeName, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!_grainActivated.Any(e => e.BehaviorTypeName == grainTypeName))
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token).ConfigureAwait(false);
        }
    }
}
