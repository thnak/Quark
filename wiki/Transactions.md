# Transactions

Quark implements Orleans-compatible ACID transactions using a two-phase commit (2PC) coordinator. Transactions span multiple grains within the same cluster.

## Overview

A transactional grain wraps its mutable state in `ITransactionalState<T>`. All reads and writes go through the transactional state object, which participates in 2PC when invoked inside a `[Transaction]`-decorated method.

## Declaring transactional state

```csharp
public sealed class BankAccountState
{
    public decimal Balance { get; set; }
}
```

```csharp
public sealed class BankAccountBehavior : IGrainBehavior, IBankAccountGrain
{
    private readonly ITransactionalState<BankAccountState> _balance;

    public BankAccountBehavior(
        [TransactionalState("balance", "transactionStore")]
        ITransactionalState<BankAccountState> balance)
    {
        _balance = balance;
    }

    [Transaction(TransactionOption.CreateOrJoin)]
    public Task DepositAsync(decimal amount)
        => _balance.PerformUpdate(s => s.Balance += amount);

    [Transaction(TransactionOption.CreateOrJoin)]
    public Task WithdrawAsync(decimal amount)
        => _balance.PerformUpdate(s =>
        {
            if (s.Balance < amount) throw new InvalidOperationException("Insufficient funds.");
            s.Balance -= amount;
        });

    [Transaction(TransactionOption.CreateOrJoin)]
    public Task<decimal> GetBalanceAsync()
        => _balance.PerformRead(s => s.Balance);
}
```

## `ITransactionalState<T>` API

```csharp
// Read-only operation — participates in the current transaction
Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction);

// Mutating operation — participates in the current transaction
Task PerformUpdate(Action<TState> updateFunction);
Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction);
```

All three overloads enlist the state in the ambient transaction. The coordinator commits or rolls back all participating states atomically.

## `[Transaction]` attribute options

```csharp
public enum TransactionOption
{
    Create,              // must start a new transaction; throws if one exists
    Join,                // must join an existing transaction; throws if none
    CreateOrJoin,        // use existing or create one (most common)
    Suppress,            // run outside any transaction
    Supported,           // join if one exists, otherwise run without
    NotAllowed           // throw if called inside a transaction
}
```

## Multi-grain transactions

Transactions span grain calls naturally — any grain method decorated with `[Transaction(Join)]` or `[Transaction(CreateOrJoin)]` automatically joins the ambient transaction:

```csharp
// Orchestrator grain
[Transaction(TransactionOption.Create)]
public async Task TransferAsync(string fromId, string toId, decimal amount)
{
    var from = _factory.GetGrain<IBankAccountGrain>(fromId);
    var to   = _factory.GetGrain<IBankAccountGrain>(toId);
    await from.WithdrawAsync(amount);
    await to.DepositAsync(amount);
    // Both grains commit or both roll back — atomically
}
```

## Registration

```csharp
// Enable the transaction coordinator
silo.Services.UseTransactions();

// Register the transaction storage provider
silo.Services.AddInMemoryGrainStorage("transactionStore");
// or Redis:
silo.Services.AddRedisGrainStorage("transactionStore", opts => { ... });
```

## Error handling

If any participating grain throws, all state changes in the transaction are rolled back. The `ITransactionalState` implementation handles the 2PC protocol transparently.

```csharp
try
{
    await orchestrator.TransferAsync("alice", "bob", 100m);
}
catch (OrleansTransactionAbortedException ex)
{
    Console.WriteLine($"Transaction aborted: {ex.Message}");
}
```

## Isolation and consistency

- **Isolation:** serializable — each transaction sees a consistent snapshot; concurrent transactions are serialized by the coordinator.
- **Durability:** committed state is written to the configured `IGrainStorage` provider.
- **Atomicity:** the 2PC coordinator ensures all-or-nothing across grains.

## Limitations

- Transactions only work within a single cluster (no cross-cluster transactions).
- All participating grains must be reachable from the same silo (or via the grain directory).
- Very long-running transactions hold locks; keep them short.
