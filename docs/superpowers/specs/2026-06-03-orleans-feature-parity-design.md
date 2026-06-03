# Orleans Feature Parity — Design Spec

**Date:** 2026-06-03
**Status:** Approved
**Scope:** Catalog every missing Orleans-compatible API surface in Quark and define what each feature requires to implement, so they can be tackled one by one.
**Orleans reference:** [`README.md`](../../../README.md)

---

## Background

Quark is an AOT-first distributed actor framework modeled on Orleans. A systematic comparison of the official Orleans sample applications (HelloWorld, BankAccount, ChatRoom, Chirper, GPSTracker, Adventure, ShoppingCart, Stocks, TicTacToe, Voting, JournaledTodoList, Streaming, TransportLayerSecurity) against Quark's current source identified 13 missing feature areas.

Each feature is tracked here with its Orleans contract, what Quark must add (API surface, runtime behavior, code-gen, tests), complexity, and dependencies.

---

## Already Covered (baseline)

| Feature | Quark location |
|---|---|
| `OnActivateAsync` / `OnDeactivateAsync` / `DeactivateOnIdle` | `Grain.cs`, `GrainContext.cs` |
| `IGrainObserver` marker interface | `IGrainObserver.cs` |
| `[StatelessWorker]` + placement | `StatelessWorkerAttribute.cs`, `PlacementDirector` |
| `Grain<TState>` single-state persistence | `Quark.Persistence.Abstractions` |
| In-memory + Redis `IGrainStorage` | `Quark.Persistence.InMemory/Redis` |
| All 5 key-type grain interfaces | `IGrainWithStringKey`, `GuidKey`, `IntKey`, compounds |
| `RequestContext` (thread-static dictionary) | `RequestContext.cs` |
| Lifecycle stages | `GrainLifecycleStage.cs` |
| Source generators (proxy / activator / serializer) | `Quark.CodeGenerator` |
| TCP transport | `Quark.Transport.Tcp` |
| Testing harness (`TestCluster`, `TestSilo`) | `Quark.Testing` |

---

## Missing Features

### F-01 — Primary Key Accessor Helpers

**Complexity:** S
**Dependencies:** none

**Orleans contract:**
```csharp
// Inside a grain:
string key   = this.GetPrimaryKeyString();
Guid   key   = this.GetPrimaryKey();
long   key   = this.GetPrimaryKeyLong();
Guid   key   = this.GetPrimaryKey(out string keyExt);
long   key   = this.GetPrimaryKeyLong(out string keyExt);
```

**What Quark needs:**
- Extension methods (or `Grain` base methods) that read the grain's `GrainId` and return the typed key.
- `GrainId` already carries a string key internally; the helpers parse it back to `Guid` / `long` as needed.
- No code-gen changes. No runtime changes.
- Locate in `Quark.Core.Abstractions` on `IGrainContext` or as extensions on `Grain`.

**Tests:** unit tests for each overload verifying round-trip from `GetGrain<T>(key)` → grain reads key back correctly.

---

### F-02 — `[Reentrant]` Attribute

**Complexity:** S–M
**Dependencies:** none (runtime scheduling change)

**Orleans contract:**
```csharp
[Reentrant]
public class MyGrain : Grain, IMyGrain { ... }
```
Reentrant grains allow a second (or more) message to interleave execution while an `await` is in progress, rather than queuing behind it.

**What Quark needs:**
- `ReentrantAttribute` in `Quark.Core.Abstractions/Grains/`.
- `GrainActivation` message pump: detect `[Reentrant]` on the grain type at activation time and switch from a strict serial channel to a concurrent dispatch path.
- `AttributePlacementStrategyResolver` or a new `GrainConcurrencyResolver` to read the attribute.
- AOT-safe: read attribute at code-gen time or via `typeof(TGrain).IsDefined(typeof(ReentrantAttribute))` — acceptable because it runs at activation, not per-call.

**Tests:** verify that a `[Reentrant]` grain dispatches concurrent calls without deadlock; verify a non-reentrant grain serialises them.

---

### F-03 — Grain Timers

**Complexity:** M
**Dependencies:** none

**Orleans contract:**
```csharp
IGrainTimer timer = RegisterGrainTimer(
    callback: TickAsync,
    state: myState,
    options: new GrainTimerCreationOptions { DueTime = ..., Period = ... });

timer.Dispose(); // cancel
```

**What Quark needs:**
- `IGrainTimer : IDisposable` interface in `Quark.Core.Abstractions`.
- `GrainTimerCreationOptions` record: `DueTime`, `Period`, `Interleave` (bool — whether timer fires even while grain is processing another message).
- `RegisterGrainTimer<TState>(Func<TState,CancellationToken,Task>, TState, GrainTimerCreationOptions)` method on `Grain` base.
- Runtime implementation in `GrainContext` or a `GrainTimerRegistry`: uses `System.Threading.Timer` internally; fires callback via the grain's message channel (respects `Interleave`).
- Timer is automatically disposed when the grain deactivates (`OnDeactivateAsync` path).
- No code-gen changes.

**Tests:** timer fires at expected interval; timer is cancelled on grain deactivation; `Interleave=false` queues timer callback behind in-progress message.

---

### F-04 — `IPersistentState<T>` + `[PersistentState]` Attribute

**Complexity:** M
**Dependencies:** none (additive to existing persistence)

**Orleans contract:**
```csharp
public class MyGrain : Grain, IMyGrain
{
    public MyGrain(
        [PersistentState("profile", "myStore")] IPersistentState<Profile> profile) { }
}
```
Allows a grain to inject multiple named state slots, each backed by a named storage provider, without inheriting `Grain<TState>`.

**What Quark needs:**
- `IPersistentState<TState>` interface: `TState State { get; set; }`, `bool RecordExists`, `Task ReadStateAsync()`, `Task WriteStateAsync()`, `Task ClearStateAsync()`.
- `[PersistentState(stateName, storageName)]` constructor-parameter attribute.
- `PersistentState<TState>` concrete implementation wrapping `IGrainStorage`.
- DI / code-gen: `GrainActivatorGenerator` must detect `[PersistentState]`-annotated constructor parameters and inject a `PersistentState<T>` instance (resolved from the named `IGrainStorage`).
- Named storage provider registry: `services.AddGrainStorage("myStore", provider)` — already partially covered by `AddInMemoryGrainStorage` / `AddRedisGrainStorage`; add optional name parameter.

**Tests:** grain with two `[PersistentState]` slots reads/writes each independently; named provider lookup resolves correctly.

---

### F-05 — `AsReference<T>()` and `CreateObjectReference<T>()`

**Complexity:** M
**Dependencies:** F-01 (grain self-identification)

**Orleans contract:**
```csharp
// From inside a grain — get a serialisable self-reference
IMyObserver self = this.AsReference<IMyObserver>();

// From outside a grain — wrap a plain object as a grain observer ref
IMyObserver objRef = grainFactory.CreateObjectReference<IMyObserver>(myObserverObject);
```

**What Quark needs:**
- `AsReference<T>()` extension on `Grain` (or on `IGrainContext`): returns a proxy for the grain's own `GrainId` typed as `T`. Requires `T` to be a registered proxy type.
- `CreateObjectReference<T>(T target)` on `IGrainFactory` / `IClusterClient`: wraps a local object that implements `IGrainObserver` in a proxy so remote grains can call it. For in-process (local) clusters this can be a direct delegate wrapper; for cross-silo it needs transport registration.
- `IGrainObserver` must remain the base constraint for `CreateObjectReference`.
- No code-gen changes for `AsReference`. `CreateObjectReference` for local (in-process) clusters: store the object in a keyed registry keyed by a synthetic `GrainId`; proxy routes calls directly to the registered object. Cross-silo `CreateObjectReference` (transport-backed) is out of scope until F-10 lands.

**Tests:** `AsReference` round-trip — grain receives call via its own reference; `CreateObjectReference` wraps plain object and routes calls correctly.

---

### F-06 — Grain Reminders

**Complexity:** L
**Dependencies:** F-03 (timers — for conceptual consistency; not a hard code dependency)

**Orleans contract:**
```csharp
public class MyGrain : Grain, IMyGrain, IRemindable
{
    public async Task ReceiveReminder(string reminderName, TickStatus status) { ... }

    async Task SetupReminder()
    {
        IGrainReminder r = await RegisterOrUpdateReminder("daily", dueTime, period);
        await UnregisterReminder(r);
        IReadOnlyList<IGrainReminder> all = await GetReminders();
    }
}
```
Reminders are **durable** — they survive silo restarts and require persistent storage.

**What Quark needs:**
- `IRemindable` interface: `Task ReceiveReminder(string reminderName, TickStatus status)`.
- `IGrainReminder`: `string ReminderName`, `bool IsValid`.
- `TickStatus` record: `FirstTickTime`, `Period`, `CurrentTickTime`.
- `RegisterOrUpdateReminder(string, TimeSpan dueTime, TimeSpan period)` / `UnregisterReminder(IGrainReminder)` / `GetReminders()` on `Grain` base.
- `IReminderService` in the runtime: responsible for scheduling, persisting, and firing reminders. Needs a storage backend (can reuse `IGrainStorage` or add `IReminderStorage`).
- In-memory `IReminderService` for testing; persistent `IReminderService` for production.
- `SiloHostedService` wires up `IReminderService` on startup.

**Tests:** reminder fires after `dueTime`; survives simulated silo restart (in-memory provider reloads state); `UnregisterReminder` stops future firings.

---

### F-07 — Streams (Pub/Sub)

**Complexity:** XL
**Dependencies:** F-02 (reentrant for subscriber grains), serialization (already done)

**Orleans contract:**
```csharp
// Producer
IAsyncStream<ChatMsg> stream = streamProvider.GetStream<ChatMsg>(StreamId.Create("chat", roomId));
await stream.OnNextAsync(msg);

// Consumer (explicit)
StreamSubscriptionHandle<ChatMsg> handle = await stream.SubscribeAsync(OnMessage);
await handle.UnsubscribeAsync();

// Consumer (implicit)
[ImplicitStreamSubscription("chat")]
public class ChatSubscriberGrain : Grain, IAsyncObserver<ChatMsg> { ... }
```

**What Quark needs:**
- `IAsyncStream<T>` interface: `OnNextAsync`, `OnErrorAsync`, `OnCompletedAsync`, `SubscribeAsync`, `GetAllSubscriptionHandles`.
- `StreamSubscriptionHandle<T>` : `UnsubscribeAsync`, `ResumeAsync`.
- `IAsyncObserver<T>` : `OnNextAsync`, `OnErrorAsync`, `OnCompletedAsync`.
- `IStreamSubscriptionObserver` : `OnSubscribed(IStreamSubscriptionHandleFactory)`.
- `StreamId` : `Create(string ns, string key)` / `Create(string ns, Guid key)`.
- `[ImplicitStreamSubscription(namespace)]` attribute.
- `IStreamProvider` / `IStreamProviderManager` in runtime.
- In-memory stream provider (`AddMemoryStreams(name)`) for initial support.
- New package: `Quark.Streaming.Abstractions` + `Quark.Streaming.InMemory`.
- Code-gen: detect `[ImplicitStreamSubscription]` and wire up subscription on activation.
- `StreamSequenceToken` for ordered delivery.

**Tests:** producer publishes; explicit subscriber receives; implicit subscriber activates and receives; unsubscribe stops delivery.

---

### F-08 — Transactions

**Complexity:** XL
**Dependencies:** F-04 (`IPersistentState<T>` — transactions wrap persistent state)

**Orleans contract:**
```csharp
public class AccountGrain : Grain, IAccountGrain
{
    public AccountGrain(
        [PersistentState("balance")] ITransactionalState<Balance> balance) { }

    [Transaction(TransactionOption.Join)]
    public Task Withdraw(decimal amount) =>
        _balance.PerformUpdate(b => b.Value -= amount);
}
// Setup:
siloBuilder.UseTransactions();
siloBuilder.AddMemoryGrainStorageAsDefault();
```

**What Quark needs:**
- `ITransactionalState<TState>` : `PerformRead<TResult>`, `PerformUpdate`, `PerformUpdate<TResult>`.
- `[Transaction(TransactionOption)]` method attribute; `TransactionOption` enum: `Create`, `Join`, `CreateOrJoin`, `Supported`, `NotAllowed`.
- Transaction coordinator in the runtime: 2-phase commit across grains in the same logical transaction.
- `UseTransactions()` silo extension.
- `TransactionalStateAttribute` (constructor-parameter attribute, similar to `[PersistentState]`).
- New package: `Quark.Transactions`.

**Tests:** single-grain transactional update commits; multi-grain transaction with rollback on failure; concurrent transactions don't corrupt state.

---

### F-09 — JournaledGrain (Event Sourcing)

**Complexity:** L
**Dependencies:** F-04 (`IPersistentState<T>` for log storage)

**Orleans contract:**
```csharp
public class TodoGrain : JournaledGrain<TodoState, TodoEvent>, ITodoGrain
{
    public Task Add(string item)
    {
        RaiseEvent(new TodoEvent.ItemAdded(item));
        return ConfirmEventsAsync();
    }
    public Task<IReadOnlyList<TodoEvent>> GetHistory() =>
        RetrieveConfirmedEvents(0, Version);
}
```

**What Quark needs:**
- `JournaledGrain<TState, TEvent>` abstract base class.
- `RaiseEvent(TEvent)` / `RaiseEvents(IEnumerable<TEvent>)`.
- `ConfirmEventsAsync()` — flushes staged events to storage.
- `RetrieveConfirmedEvents(int from, int to)` — reads history from log.
- `Version` property.
- `TState` must implement `Apply(TEvent)` (Orleans uses a convention-based `Apply` method).
- Log consistency providers: `StateStorageBasedLogConsistencyProvider`, `LogStorageBasedLogConsistencyProvider`.
- New package or module: `Quark.EventSourcing`.

**Tests:** events are raised and applied to state; `ConfirmEventsAsync` persists; `RetrieveConfirmedEvents` returns correct history.

---

### F-10 — Real Multi-Silo Clustering

**Complexity:** XL
**Dependencies:** Transport layer (done), `ILocalSiloDetails` (F-12)

**Orleans contract:**
```csharp
siloBuilder
    .UseLocalhostClustering(siloPort: 11111, gatewayPort: 30000, primarySiloEndpoint: ...)
    .UseCosmosClustering(...)
```

**Current state:** `UseLocalhostClustering()` in Quark is a no-op stub. `IGrainDirectory` exists but only in-memory single-node.

**What Quark needs:**
- Membership table abstraction: `IMembershipTable` with `ReadAll`, `InsertRow`, `UpdateRow`, `UpdateIAmAlive`.
- In-memory `IMembershipTable` (for localhost clustering, single process — works for M1/M2).
- Membership oracle: background service that periodically writes `IAmAlive`, detects dead silos, and triggers grain directory cleanup.
- Distributed `IGrainDirectory`: today it's in-memory per-silo; needs cross-silo lookup forwarding.
- `UseLocalhostClustering()` should wire all of the above for single-process multi-silo tests.
- Future: Redis/SQL membership providers as separate packages.

**Tests:** two silos in a `TestCluster`; grain activated on silo A is reachable from silo B's client; silo B shutdown re-activates grain on silo A.

---

### F-11 — OpenTelemetry / `AddActivityPropagation()`

**Complexity:** S
**Dependencies:** none

**Orleans contract:**
```csharp
siloBuilder.AddActivityPropagation();
```
Injects `Activity`/`ActivitySource` around grain calls so distributed tracing spans flow across grains and silos.

**What Quark needs:**
- `AddActivityPropagation()` extension on `ISiloBuilder`.
- `DiagnosticActivity` wrapper or middleware in `LocalGrainCallInvoker` / `MessageDispatcher`: start a child `Activity` per grain call, tag with `grain.type`, `grain.id`, `grain.method`.
- Use `System.Diagnostics.ActivitySource` (AOT-safe, no reflection).
- No new package — fits in `Quark.Runtime` or a thin `Quark.Diagnostics` module.

**Tests:** calling a grain produces an `Activity` with expected tags; nested grain calls produce parent–child spans.

---

### F-12 — `ILocalSiloDetails`

**Complexity:** S
**Dependencies:** F-10 (clustering — needs real silo identity)

**Orleans contract:**
```csharp
// Injected into grain constructor:
ILocalSiloDetails siloDetails
// Properties: SiloAddress, Name, ClusterId, ServiceId
```

**What Quark needs:**
- `ILocalSiloDetails` interface in `Quark.Core.Abstractions`: `SiloAddress SiloAddress`, `string Name`, `string ClusterId`, `string ServiceId`.
- `LocalSiloDetails` concrete class registered as singleton in `AddQuarkRuntime()`.
- `SiloAddress` already exists in `Quark.Core.Abstractions`.

**Tests:** `ILocalSiloDetails` resolves from DI with correct values matching `UseLocalhostClustering` config.

---

### F-13 — TLS Transport (`UseTls()`)

**Complexity:** L
**Dependencies:** F-10 (multi-silo clustering — TLS only matters with real silo-to-silo connections)

**Orleans contract:**
```csharp
siloBuilder.UseTls(options =>
{
    options.LocalCertificate = cert;
    options.OnAuthenticateAsClient = (conn, sslOptions) => { ... };
    options.AllowAnyRemoteCertificate();
});
```

**What Quark needs:**
- `TlsOptions` class: `LocalCertificate`, `RemoteCertificateMode`, `OnAuthenticateAsClient`, `OnAuthenticateAsServer`.
- `UseTls(ISiloBuilder, Action<TlsOptions>)` extension.
- `TcpTransport` / `TcpTransportConnection` must wrap the `NetworkStream` in `SslStream` when TLS is configured.
- Helper: `RemoteCertificateMode` enum: `NoCertificate`, `AllowAny`, `RequireCertificate`.
- New package: `Quark.Transport.Tcp.Tls` or add as optional path in `Quark.Transport.Tcp`.

**Tests:** two silos connect with self-signed certificates; connection fails with mismatched certificates when `RequireCertificate` is set.

---

## Implementation Order

```
Phase 1 — Low-hanging fruit (no inter-feature deps)
  F-01  GetPrimaryKey helpers          S
  F-02  [Reentrant] attribute          S–M
  F-03  Grain Timers                   M
  F-11  OpenTelemetry propagation      S

Phase 2 — Persistence extension
  F-04  IPersistentState<T>            M
  F-05  AsReference / CreateObjectRef  M

Phase 3 — Advanced grain patterns
  F-06  Grain Reminders                L
  F-09  JournaledGrain                 L

Phase 4 — Distributed infrastructure
  F-10  Real multi-silo clustering     XL
  F-12  ILocalSiloDetails              S  (after F-10)
  F-13  TLS transport                  L  (after F-10)

Phase 5 — Complex subsystems
  F-07  Streams                        XL
  F-08  Transactions                   XL
```

---

## Tracking

All features are also listed in `FEATURES.md` at the repo root with checkboxes, so progress is visible without an external tracker.
