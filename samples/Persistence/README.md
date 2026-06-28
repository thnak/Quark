# Persistence sample — "Bank"

An end-to-end silo + TCP client that demonstrates **five of Quark's persistence / activation-memory
patterns** side by side, all keyed by the same account id:

| Grain | Pattern | Interface / base | What it shows |
|---|---|---|---|
| `IAccountGrain` | Persistent activation memory | `IPersistentActivationMemory<AccountState>` | Balance cached in the shell, write-through to storage, read with **no** round-trip |
| `IProfileGrain` | Named persistent state (Orleans-style) | `[PersistentState("profile")] IPersistentState<ProfileState>` | A direct storage slot you `ReadStateAsync`/`WriteStateAsync` per use |
| `ILedgerGrain` | Event sourcing | `JournaledGrain<LedgerState, LedgerEvent>` | Append-only event log; balance + history rebuilt by replaying events |
| `IVaultGrain` | Eager activation memory | `IEagerActivationMemory<RateSnapshot>` | DI-aware factory runs **before** `OnActivateAsync`; value read **synchronously** |
| `IStatementGrain` | Managed activation memory | `IManagedActivationMemory<StatementBuffer>` | Lazily **async**-initialized resource, cleaned up on deactivation |

> The last two patterns are *activation memory*, not storage: they are **not persisted**. They cover
> resources that must be (re)built per activation — `IEagerActivationMemory<T>` is the newest
> addition, filling the gap between plain `IActivationMemory<T>` (sync, no DI) and
> `IManagedActivationMemory<T>` (lazy async, but no DI in the factory).

## Run it

```bash
# Terminal 1 — start the silo (gateway on port 30005)
dotnet run --project samples/Persistence/Bank.Server

# Terminal 2 — interactive client
dotnet run --project samples/Persistence/Bank.Client
```

Pick an account id (e.g. `alice`), then try:

```
deposit 100            # persistent activation memory
withdraw 30
balance
profile Alice a@x.io   # named persistent state
whoami
credit 200 salary      # event sourcing
debit 75 rent
ledger
history
vault 10000            # eager activation memory (DI-loaded rate)
accrue 30              # apply the pinned daily rate over 30 days
rate                   # show the rate pinned from DI at activation
note opening deposit   # managed activation memory (lazy async buffer)
statement
close                  # deactivate → watch the SERVER console for the flush
```

Type `help` for the full command list, `quit` to exit.

## How each pattern is wired

Everything is registered explicitly (no assembly scanning). The server's `Program.cs`:

```csharp
silo.Services.AddInMemoryGrainStorage();                      // default IGrainStorage
silo.Services.AddSingleton<ILogStorage, InMemoryLogStorage>(); // event log
silo.Services.AddBankStateCopiers();                          // IDeepCopier<T> for stored state
silo.Services.AddSingleton<IInterestRateService, InterestRateService>(); // DI for the eager factory
silo.Services.AddBankGrainsBehaviors();                       // generated registration
```

`AddBankGrainsBehaviors()` is emitted by the **BehaviorRegistrationGenerator**. From the five
behavior classes it registers the behaviors, their TCP transport dispatchers, and every activation
accessor it finds in a constructor — `IActivationMemory` / `IPersistentActivationMemory` /
`IPersistentState` / `IEagerActivationMemory` / `IManagedActivationMemory` — including the
`IActivationMemory<JournaledGrainState<…>>` the journaled grain needs. The only things you wire by
hand are the storage providers, the copiers, and the plain DI service the eager factory resolves. The
client's `AddBankGrainInterfacesGrainProxies()` is emitted by the **ClientProxyRegistrationGenerator**.

### 1. Persistent activation memory — `AccountBehavior`

State lives in the activation **shell**, so it is shared across every call to the same activation.
Load it once in `OnActivateAsync`, read `Value` freely, and `SaveAsync()` after a mutation to flush
write-through to storage so it survives deactivation.

### 2. Named persistent state — `ProfileBehavior`

`[PersistentState("profile")]` resolves an `IPersistentState<ProfileState>` over the `"Default"`
provider. Because behaviors are constructed per call, this handle is **not** cached across calls
like activation memory is — it is a thin wrapper over a storage record. Call `ReadStateAsync()` when
you need the latest value and `WriteStateAsync()` after you change it; `RecordExists` reports whether
anything has been saved yet.

### 3. Event sourcing — `LedgerBehavior`

Extends `JournaledGrain<LedgerState, LedgerEvent>`. `CreditAsync`/`DebitAsync` `RaiseEvent` (applied
to the in-memory projection at once) then `ConfirmEventsAsync` to append to `ILogStorage`. The
balance and history are never stored directly — on activation the base class replays the whole event
log through `TransitionState` to rebuild them, so the full audit trail is always recoverable.
`Version` is the count of confirmed events.

### 4. Eager activation memory — `VaultBehavior`

`IEagerActivationMemory<RateSnapshot>` is the newest pattern. The constructor calls
`rate.Load((sp, ct) => …)`; that factory runs **eagerly, before `OnActivateAsync`**, inside the
activation scope, so it can **resolve DI services** (`sp.GetRequiredService<IInterestRateService>()`).
After activation the value is read **synchronously** via `Value` — no `await` — so business logic like
`AccrueAsync` applies the rate directly. The rate is captured once per activation ("pinned"), and an
optional `Destroy` callback runs after `OnDeactivateAsync`. This is the gap-filler between plain
`IActivationMemory<T>` (sync, default-constructed, no DI) and managed memory below.

### 5. Managed activation memory — `StatementBehavior`

`IManagedActivationMemory<StatementBuffer>` is for resources that need **async** initialization but
must not survive deactivation (an open writer, a pooled buffer, a cached projection). The constructor
calls `buffer.Init(async () => …).Destroy(b => …)`. The factory runs **lazily on the first
`GetAsync()`** (note: it takes no `IServiceProvider`, unlike the eager factory), the value is cached
for the activation, and `Destroy` runs after `OnDeactivateAsync`. Run `note …` a few times (init count
stays `1`), then `close` to deactivate — the `Destroy` callback prints the flush to the **server
console**, demonstrating deterministic cleanup.

## Serialization & copiers

State written through `IGrainStorage` is snapshotted with an `IDeepCopier<T>`, so `AccountState` and
`ProfileState` carry `[GenerateSerializer]` + `[Id(…)]`. The generator emits an internal
`{Type}Copier`, registered in `BankStateCopiers.AddBankStateCopiers()`. `LedgerState` needs no
serializer — it lives only in activation memory and is rebuilt from events, which the in-memory log
keeps by reference.

## Going durable (Redis)

The in-memory providers reset when the silo process stops. To persist across restarts, swap the two
storage lines in the server for their Redis equivalents (add a reference to
`Quark.Persistence.Redis` and `Quark.Reminders.Redis` as needed):

```csharp
silo.Services.AddRedisGrainStorage(o => o.ConnectionString = "localhost:6379");
// (a Redis-backed ILogStorage can be wired the same way for the ledger)
```

No grain code changes — only the provider registration.
