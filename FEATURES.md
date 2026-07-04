# Quark — Orleans Feature Parity Tracker

Missing features identified by comparing against official Orleans samples.
Each item links to the design spec for implementation details.

Design spec: [`docs/superpowers/specs/2026-06-03-orleans-feature-parity-design.md`](docs/superpowers/specs/2026-06-03-orleans-feature-parity-design.md)
Orleans architecture reference: [`README.md`](README.md)

---

## Phase 1 — Low-hanging fruit

- [x] **F-01** `GetPrimaryKeyString()` / `GetPrimaryKey()` / `GetPrimaryKeyLong()` helpers on `Grain` base — _Complexity: S_
- [x] **F-02** `[Reentrant]` attribute + concurrent dispatch in `GrainActivation` — _Complexity: S–M_
- [x] **F-03** Grain Timers (`RegisterGrainTimer`, `IGrainTimer`, `GrainTimerCreationOptions`) — _Complexity: M_
- [x] **F-11** `AddActivityPropagation()` / OpenTelemetry span propagation — _Complexity: S_

## Phase 2 — Persistence extension

- [x] **F-04** `IPersistentState<T>` + `[PersistentState("name","provider")]` attribute + named storage registry — _Complexity: M_
- [x] **F-05** `AsReference<T>()` (grain self-reference) + `CreateObjectReference<T>()` (observer wrapper) — _Complexity: M_

## Phase 3 — Advanced grain patterns

- [x] **F-06** Grain Reminders (`IRemindable`, `RegisterOrUpdateReminder`, `IGrainReminder`, durable `IReminderService`) — _Complexity: L_
- [x] **F-09** `JournaledGrain<TState,TEvent>` (event sourcing, `RaiseEvent`, `ConfirmEventsAsync`, `RetrieveConfirmedEvents`) — _Complexity: L_

## Phase 4 — Distributed infrastructure

- [x] **F-10** Real multi-silo clustering (`IMembershipTable`, membership oracle, distributed `IGrainDirectory`) — _Complexity: XL_
- [x] **F-12** `ILocalSiloDetails` (silo address/name/cluster metadata injectable into grains) — _Complexity: S_ _(after F-10)_
- [x] **F-13** TLS transport (`UseTls()`, `TlsOptions`, `SslStream` integration in TCP transport) — _Complexity: L_ _(after F-10)_

## Phase 5 — Complex subsystems

- [x] **F-07** Streams (`IAsyncStream<T>`, `IAsyncObserver<T>`, `ImplicitStreamSubscription`, in-memory stream provider) — _Complexity: XL_
- [x] **F-08** Transactions (`ITransactionalState<T>`, `[Transaction]`, 2-phase commit coordinator, `UseTransactions()`) — _Complexity: XL_

## Phase 6 — Grain lifecycle management

- [x] **F-14** Idle-timeout grain collector (`GrainCollectionAge`, `GrainCollectionInterval`, `GrainIdleCollector`) + `DelayDeactivation(TimeSpan)` — _Complexity: M_

## Phase 7 — TCP gateway client

- [x] **F-15** TCP Gateway Client (`TcpGatewayClusterClient`, `UseLocalhostGateway()`, `GatewayMessagePump`, `Quark.Client.Tcp`, grain-ref serialisation in code generator) — _Complexity: L_

## Phase 8 — Silo-to-silo transport

- [x] **F-16** Networked silo-to-silo grain forwarding (`NetworkedSiloRouter`, `SiloCallInvoker`, `SiloPeerConnection`, `PeerConnectionManager`, `IClusterMembershipSnapshot`, `x-quark-hop` loop guard, placement-director integration in `LocalGrainCallInvoker`, `AddSiloToSiloTransport()`) — closes #126 — _Complexity: L_

## Phase 9 — Grain supervision

- [x] **F-17** Cascading termination (`IActivationChildren.Attach`, `ChildTerminationMode`, `ChildRegistry`, `DeactivationReason.CascadesToChildren`, `DeactivationReason.ParentTerminated`, `IActivationTerminator`, `DefaultActivationTerminator`, one-way `TerminateRequest` frame, `MessageType.TerminateRequest`, `OnChildTerminationFailed` diagnostic) — closes #120 — _Complexity: M_

## Phase 10 — Engine-owned activation scheduling

- [x] **F-18** Single-node activation scheduler (`IActivationScheduler`, `ActivationScheduler` with configurable concurrency cap/drain budget/bounded ready queue, `SchedulerOverloadMode`, `SchedulerOverloadException`, scheduler diagnostics + `QuarkInstruments` metrics, `ReentrantSchedulingMode.Immediate` compatibility policy) — replaces the permanent per-activation processing loop with centralized, fair, engine-owned dispatch; closes #136 phases 1–5 — _Complexity: L_
- [x] **F-19** Stateless-worker pool policy (`StatelessWorkerPool`, `StatelessWorkerRouter`, `StatelessWorkerLease`, `StatelessWorkerIdentity`, `StatelessWorkerPoolPolicy`, `SiloRuntimeOptions.StatelessWorker*` options) — gives `[StatelessWorker]` grains real multiplicity: synthetic per-worker `GrainId`s (`W_i = key + SENTINEL + i`, using the ASCII Unit Separator as a reserved delimiter) route through the existing `GrainActivationTable`/`ActivationScheduler`/placement machinery unmodified, with a per-logical-grain slot pool bounding concurrent worker activations and `SchedulerOverloadMode` governing admission when the pool is saturated — closes #136 phase 6 (design spec §12, blueprint at `docs/superpowers/specs/2026-07-03-stateless-worker-pool-phase6-blueprint.md`) — _Complexity: M_
