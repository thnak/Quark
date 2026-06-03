# Quark — Orleans Feature Parity Tracker

Missing features identified by comparing against official Orleans samples.
Each item links to the design spec for implementation details.

Design spec: [`docs/superpowers/specs/2026-06-03-orleans-feature-parity-design.md`](docs/superpowers/specs/2026-06-03-orleans-feature-parity-design.md)
Orleans architecture reference: [`README.md`](README.md)

---

## Phase 1 — Low-hanging fruit

- [ ] **F-01** `GetPrimaryKeyString()` / `GetPrimaryKey()` / `GetPrimaryKeyLong()` helpers on `Grain` base — _Complexity: S_
- [ ] **F-02** `[Reentrant]` attribute + concurrent dispatch in `GrainActivation` — _Complexity: S–M_
- [ ] **F-03** Grain Timers (`RegisterGrainTimer`, `IGrainTimer`, `GrainTimerCreationOptions`) — _Complexity: M_
- [ ] **F-11** `AddActivityPropagation()` / OpenTelemetry span propagation — _Complexity: S_

## Phase 2 — Persistence extension

- [ ] **F-04** `IPersistentState<T>` + `[PersistentState("name","provider")]` attribute + named storage registry — _Complexity: M_
- [ ] **F-05** `AsReference<T>()` (grain self-reference) + `CreateObjectReference<T>()` (observer wrapper) — _Complexity: M_

## Phase 3 — Advanced grain patterns

- [ ] **F-06** Grain Reminders (`IRemindable`, `RegisterOrUpdateReminder`, `IGrainReminder`, durable `IReminderService`) — _Complexity: L_
- [ ] **F-09** `JournaledGrain<TState,TEvent>` (event sourcing, `RaiseEvent`, `ConfirmEventsAsync`, `RetrieveConfirmedEvents`) — _Complexity: L_

## Phase 4 — Distributed infrastructure

- [ ] **F-10** Real multi-silo clustering (`IMembershipTable`, membership oracle, distributed `IGrainDirectory`) — _Complexity: XL_
- [ ] **F-12** `ILocalSiloDetails` (silo address/name/cluster metadata injectable into grains) — _Complexity: S_ _(after F-10)_
- [ ] **F-13** TLS transport (`UseTls()`, `TlsOptions`, `SslStream` integration in TCP transport) — _Complexity: L_ _(after F-10)_

## Phase 5 — Complex subsystems

- [ ] **F-07** Streams (`IAsyncStream<T>`, `IAsyncObserver<T>`, `ImplicitStreamSubscription`, in-memory stream provider) — _Complexity: XL_
- [ ] **F-08** Transactions (`ITransactionalState<T>`, `[Transaction]`, 2-phase commit coordinator, `UseTransactions()`) — _Complexity: XL_
