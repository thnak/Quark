using Quark.Core.Abstractions.Grains;

namespace Bank.GrainInterfaces;

/// <summary>
///     An append-only transaction ledger built on <c>JournaledGrain&lt;LedgerState, LedgerEvent&gt;</c>.
///     Every credit/debit is recorded as an event in an <c>ILogStorage</c> log; the running
///     balance is derived by replaying those events. On reactivation the grain rebuilds its
///     state from the full event history — nothing but the events is ever persisted.
/// </summary>
public interface ILedgerGrain : IGrainWithStringKey
{
    /// <summary>Appends a <c>Credited</c> event and confirms it to the log. Returns the new balance.</summary>
    Task<decimal> CreditAsync(decimal amount, string note);

    /// <summary>Appends a <c>Debited</c> event (throws on overdraft) and confirms it. Returns the new balance.</summary>
    Task<decimal> DebitAsync(decimal amount, string note);

    /// <summary>Returns the balance derived from the confirmed event stream.</summary>
    Task<decimal> GetBalanceAsync();

    /// <summary>Returns the number of confirmed (persisted) events — the ledger's version.</summary>
    Task<int> GetVersionAsync();

    /// <summary>Returns the full audit trail as a newline-delimited string.</summary>
    Task<string> GetHistoryAsync();
}
