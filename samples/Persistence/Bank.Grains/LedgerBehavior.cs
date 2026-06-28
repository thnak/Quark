using Bank.GrainInterfaces;
using Quark.Core.Abstractions.Hosting;
using Quark.Persistence.Abstractions.Journaling;

namespace Bank.Grains;

/// <summary>
///     Pattern 3 — <b>Event sourcing</b> with <see cref="JournaledGrain{TState,TEvent}" />.
///     <para>
///         Mutations <c>RaiseEvent</c> (applied to the in-memory projection immediately) and then
///         <c>ConfirmEventsAsync</c> to append them to the <c>ILogStorage</c> log. The balance and
///         history are never stored directly — they are rebuilt by replaying the event log in
///         <c>OnActivateAsync</c> (handled by the base class). The full audit trail is always
///         recoverable.
///     </para>
/// </summary>
public sealed class LedgerBehavior : JournaledGrain<LedgerState, LedgerEvent>, ILedgerGrain
{
    // The code generator registers IActivationMemory<JournaledGrainState<LedgerState, LedgerEvent>>
    // for this constructor; ICallContext and ILogStorage come from the runtime / DI.
    public LedgerBehavior(
        IActivationMemory<JournaledGrainState<LedgerState, LedgerEvent>> memory,
        ICallContext ctx,
        ILogStorage? log = null)
        : base(memory, ctx, log) { }

    // Pure function: how each event mutates the projection. Used for both live updates and replay.
    protected override void TransitionState(LedgerState state, LedgerEvent @event)
    {
        switch (@event)
        {
            case Credited c:
                state.Balance += c.Amount;
                state.History.Add($"+ {c.Amount:C}  {c.Note}");
                break;
            case Debited d:
                state.Balance -= d.Amount;
                state.History.Add($"- {d.Amount:C}  {d.Note}");
                break;
        }
    }

    public async Task<decimal> CreditAsync(decimal amount, string note)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Credit must be positive.");
        RaiseEvent(new Credited(amount, note));
        await ConfirmEventsAsync();
        return State.Balance;
    }

    public async Task<decimal> DebitAsync(decimal amount, string note)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Debit must be positive.");
        if (amount > State.Balance)
            throw new InvalidOperationException($"Overdraft blocked: balance is {State.Balance:C}, requested {amount:C}.");
        RaiseEvent(new Debited(amount, note));
        await ConfirmEventsAsync();
        return State.Balance;
    }

    public Task<decimal> GetBalanceAsync() => Task.FromResult(State.Balance);

    public Task<int> GetVersionAsync() => Task.FromResult(Version);

    public Task<string> GetHistoryAsync() =>
        Task.FromResult(State.History.Count == 0
            ? "(no transactions yet)"
            : string.Join(Environment.NewLine, State.History));
}
