using Quark.Core.Abstractions.Grains;

namespace Bank.GrainInterfaces;

/// <summary>
///     A savings account whose balance is kept durable with
///     <c>IPersistentActivationMemory&lt;AccountState&gt;</c>: the balance lives in the
///     activation shell (read with no storage round-trip) and is flushed to the configured
///     <c>IGrainStorage</c> provider after every mutation, so it survives deactivation.
/// </summary>
public interface IAccountGrain : IGrainWithStringKey
{
    /// <summary>Adds <paramref name="amount" /> to the balance and persists. Returns the new balance.</summary>
    Task<decimal> DepositAsync(decimal amount);

    /// <summary>Removes <paramref name="amount" />; throws if funds are insufficient. Returns the new balance.</summary>
    Task<decimal> WithdrawAsync(decimal amount);

    /// <summary>Returns the current balance straight from activation memory (no storage read).</summary>
    Task<decimal> GetBalanceAsync();

    /// <summary>Returns how many deposits/withdrawals have ever been applied to this account.</summary>
    Task<int> GetTransactionCountAsync();
}
