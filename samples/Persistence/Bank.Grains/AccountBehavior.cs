using Bank.GrainInterfaces;
using Quark.Core.Abstractions.Grains;
using Quark.Persistence.Abstractions;

namespace Bank.Grains;

/// <summary>
///     Pattern 1 — <b>Persistent activation memory</b>.
///     <para>
///         The balance is held in the activation shell via
///         <see cref="IPersistentActivationMemory{TState}" />. Reads (<see cref="GetBalanceAsync" />)
///         hit memory with no storage round-trip; mutations call <c>SaveAsync</c> so the value is
///         flushed write-through to the configured <c>IGrainStorage</c> provider and survives
///         deactivation. State is loaded once in <see cref="OnActivateAsync" />.
///     </para>
/// </summary>
public sealed class AccountBehavior : IGrainBehavior, IAccountGrain, IActivationLifecycle
{
    private readonly IPersistentActivationMemory<AccountState> _memory;

    public AccountBehavior(IPersistentActivationMemory<AccountState> memory) => _memory = memory;

    private AccountState S => _memory.Value;

    // Load persisted state once, when the activation is first created.
    public Task OnActivateAsync(CancellationToken ct) => _memory.LoadAsync(ct);

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

    public async Task<decimal> DepositAsync(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Deposit must be positive.");
        S.Balance += amount;
        S.TransactionCount++;
        await _memory.SaveAsync();
        return S.Balance;
    }

    public async Task<decimal> WithdrawAsync(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Withdrawal must be positive.");
        if (amount > S.Balance)
            throw new InvalidOperationException($"Insufficient funds: balance is {S.Balance:C}, requested {amount:C}.");
        S.Balance -= amount;
        S.TransactionCount++;
        await _memory.SaveAsync();
        return S.Balance;
    }

    // No storage round-trip — the shell already holds the current value.
    public Task<decimal> GetBalanceAsync() => Task.FromResult(S.Balance);

    public Task<int> GetTransactionCountAsync() => Task.FromResult(S.TransactionCount);
}
