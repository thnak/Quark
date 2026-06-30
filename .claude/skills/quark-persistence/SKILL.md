---
name: quark-persistence
description: Use when a Quark grain needs state — choosing among the 5 patterns (IActivationMemory, IManagedActivationMemory, IPersistentActivationMemory, [PersistentState] named slot, JournaledGrain event sourcing) and registering storage providers (in-memory/Redis). Quark-specific.
---

# Quark Persistence

## Overview

Five state patterns, ephemeral → fully event-sourced. Pick by durability + access pattern.

| Pattern | Inject | Durable? | Cached in shell? | Use when |
|---|---|---|---|---|
| Activation memory | `IActivationMemory<T>` | No | Yes | survives across calls, not deactivation |
| Managed memory | `IManagedActivationMemory<T>` | No | Yes (lazy) | async-init resource needing cleanup on deactivation |
| Persistent activation memory | `IPersistentActivationMemory<T>` | Yes | Yes (write-through) | durable state, fast reads, explicit save |
| Named state slot | `[PersistentState("name")] IPersistentState<T>` | Yes | **No** (per-call handle) | Orleans-compatible named storage |
| Event sourcing | inherit `JournaledGrain<TState,TEvent>` | Yes (log) | Yes (projection) | append-only audit log, replay on activation |

State types that are persisted must be `[GenerateSerializer]` with stable `[Id]`s (provider deep-copies them).

## Pattern 1 — Persistent activation memory (shell-cached, write-through)

```csharp
public sealed class AccountBehavior : IGrainBehavior, IAccountGrain, IActivationLifecycle
{
    private readonly IPersistentActivationMemory<AccountState> _memory;
    public AccountBehavior(IPersistentActivationMemory<AccountState> memory) => _memory = memory;
    private AccountState S => _memory.Value;

    public Task OnActivateAsync(CancellationToken ct) => _memory.LoadAsync(ct);   // load once
    public Task OnDeactivateAsync(DeactivationReason r, CancellationToken ct) => Task.CompletedTask;

    public async Task<decimal> DepositAsync(decimal amount)
    {
        S.Balance += amount;
        await _memory.SaveAsync();      // write-through; survives deactivation
        return S.Balance;
    }
    public Task<decimal> GetBalanceAsync() => Task.FromResult(S.Balance);  // no storage round-trip
}
```

## Pattern 2 — Named state slot (`[PersistentState]`, per-call handle, NOT cached)

```csharp
public sealed class ProfileBehavior : IGrainBehavior, IProfileGrain
{
    private readonly IPersistentState<ProfileState> _profile;
    public ProfileBehavior([PersistentState("profile")] IPersistentState<ProfileState> profile)
        => _profile = profile;

    public async Task UpdateAsync(string name)
    {
        _profile.State.DisplayName = name;
        await _profile.WriteStateAsync();
    }
    public async Task<string> DescribeAsync()
    {
        await _profile.ReadStateAsync();                 // re-read each call — NOT shell-cached
        return _profile.RecordExists ? _profile.State.DisplayName : "(none)";
    }
}
```

## Pattern 3 — Event sourcing (`JournaledGrain<TState,TEvent>`)

```csharp
public sealed class LedgerBehavior : JournaledGrain<LedgerState, LedgerEvent>, ILedgerGrain
{
    public LedgerBehavior(
        IActivationMemory<JournaledGrainState<LedgerState, LedgerEvent>> memory,
        ICallContext ctx,
        ILogStorage? log = null) : base(memory, ctx, log) { }

    protected override void TransitionState(LedgerState state, LedgerEvent e)   // pure; used live + on replay
    {
        if (e is Credited c) state.Balance += c.Amount;
    }

    public async Task<decimal> CreditAsync(decimal amount)
    {
        RaiseEvent(new Credited(amount));   // applies to projection immediately
        await ConfirmEventsAsync();         // appends to ILogStorage
        return State.Balance;               // base-class projection; Version is the event count
    }
}
```

## Pattern 4 — Managed activation memory (async init + deterministic cleanup)

```csharp
public StatementBehavior(IManagedActivationMemory<StatementBuffer> buffer, ...)
{
    _buffer = buffer
        .Init(async () => { await OpenAsync(); return new StatementBuffer(); })  // lazy, on first GetAsync
        .Destroy(b => { Flush(b); return ValueTask.CompletedTask; });            // after OnDeactivateAsync
}
public async Task AddLineAsync(string line) => (await _buffer.GetAsync()).Lines.Add(line);
```

## Pattern 5 — Plain activation memory

`IActivationMemory<T>` (`.Value`) — see quark-writing-grains. No durability; lost on deactivation.

## Storage provider registration (silo)

```csharp
silo.Services.AddInMemoryGrainStorage();                       // default provider
silo.Services.AddInMemoryGrainStorage("named");                // named
silo.Services.AddSingleton<ILogStorage, InMemoryLogStorage>(); // for JournaledGrain
silo.Services.AddRedisGrainStorage(o => o.ConnectionString = "localhost:6379");      // durable, one line swap
silo.Services.AddRedisReminderService(o => o.ConnectionString = "localhost:6379");
```

The generated `Add{Assembly}Behaviors()` wires the `IPersistentActivationMemory<T>` / `IPersistentState<T>` / journaled-memory accessors automatically — you only register the **storage providers** + deep copiers.

## Common mistakes

- **Expecting `[PersistentState]` to cache.** It's a per-call handle — `ReadStateAsync` every call. Use `IPersistentActivationMemory` for shell caching.
- **Forgetting to `LoadAsync` in `OnActivateAsync`** for persistent activation memory → stale/empty state.
- **Mutating without `SaveAsync`/`WriteStateAsync`/`ConfirmEventsAsync`** → lost on deactivation.
- **Non-serializable state type** → storage can't deep-copy. Add `[GenerateSerializer]` + `[Id]`.
- **`TransitionState` with side effects.** It must be pure — it runs again on every replay.

## Related skills

- quark-writing-grains — behavior/state scaffold
- quark-host-setup — provider registration, transactions
