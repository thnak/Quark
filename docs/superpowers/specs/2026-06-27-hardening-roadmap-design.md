# Quark Hardening Roadmap — "Trust · Safe · Fast" vs Orleans

**Date:** 2026-06-27
**Status:** Approved design — tracked as GitHub epics + child issues in `thnak/Quark`
**Author:** Engineering (brainstormed with Claude Code)

## 1. Purpose & framing

Quark has reached broad Orleans feature parity (Phases 1–7: persistence, reminders,
streams, transactions, multi-silo clustering, TLS, event sourcing, TCP gateway). It is
**not** missing features. What it lacks is the thing a young framework cannot borrow from
a competitor: **proof**. Orleans earns trust from a decade of production hardening at
Microsoft scale; Quark must earn it from **measured, demonstrable** correctness, safety,
and performance.

This roadmap is an **engineering hardening backlog**. Every item is grounded in a concrete
gap found by auditing the current codebase (not generic best practices), and every item has
a **measurable exit criterion**. The strategy:

- **Safe** and **Fast** are where an AOT-first design can *beat* Orleans and prove it
  quickly — these are differentiators.
- **Trust** is the hardest and slowest; for a newcomer it comes only from proof:
  fault-injection coverage, delivery-guarantee semantics, soak/chaos tests, and published
  benchmarks.

Scope chosen: **balanced sweep** across four tracks, **thoroughness over speed** (no hard
deadline). Deliverable: this spec + GitHub issues. **No production code is changed by this
roadmap document itself.**

## 2. Audit method & a note on rigor

Three parallel auditors swept the codebase for Trust, Fast, and Safe gaps, each citing
`file:line`. Findings were then **spot-verified by hand** before being promoted to issues.
Two outcomes worth recording:

- **Confirmed real:** `MessageSerializer.TryReadEnvelope` (`MessageSerializer.cs:113-120`)
  reads a signed `int` payload length straight off the wire with no maximum-size or
  overflow guard; `CodecReader.ReadString`/`ReadBytes` (`CodecReader.cs:133,146`) perform
  unchecked `(int)byteCount` casts with no cap. These are genuine wire-hardening gaps.
- **Rejected as false:** an auditor claimed grain-method exceptions are silently swallowed
  in `GrainActivation`'s run loop. Verified false — `PostAsync` (`GrainActivation.cs:280-308`)
  routes the result/exception through a `TaskCompletionSource` the caller awaits
  (`tcs.TrySetException(ex)` → `await tcs.Task`); the run-loop `catch` is a logging backstop.
  This finding was dropped.

The lesson encoded into the roadmap: findings become issues only after verification.

## 3. Tracks & epics

Four epics. Severity reflects urgency for a networked `0.1.0`. Recommended sequence:
**A-criticals → C1 (benchmark foundation) → D1 (CI gate) → everything else in parallel.**

### Track A — SAFE (security & robustness) — *sequence first*

Concrete, cheap, and blocking: several are genuine vulnerabilities for any networked
deployment.

| ID | Title | Severity | Evidence | Exit criterion |
|----|-------|----------|----------|----------------|
| **A1** | Wire-protocol hardening | CRITICAL | `MessageSerializer.cs:113-120`; `CodecReader.cs:133,146`; header loop `MessageSerializer.cs:45-52` | Configurable max message size; overflow-safe (`long`) length math; capped varint reads for string/bytes/header-count; deserialization recursion-depth limit. Hostile-input/fuzz tests pass with no OOM, hang, or crash. |
| **A2** | Resource-exhaustion limits | CRITICAL / HIGH | `GrainActivationTable` (unbounded `ConcurrentDictionary`); `GrainActivation.cs:40` (unbounded mailbox); `TcpTransportListener.cs:37` + pump connection lists | Configurable activation cap, bounded mailbox with explicit backpressure policy, max-concurrent-connection limit. Mass-activation & connection-flood tests stay bounded. |
| **A3** | Transport authentication + TLS hardening | CRITICAL | No client/silo authN before dispatch (`MessageDispatcher`, `GatewayMessagePump`); `TcpTransport.cs:84-89` `AllowAny => (_,_,_,_) => true`; no client hostname/SAN check | Pluggable authN SPI (TLS-cert-identity or token); `AllowAny` fails secure / is dev-only & loudly logged; client validates hostname/SAN; mTLS round-trip test. |
| **A4** | Fault isolation & info-disclosure | HIGH | `MessageDispatcher.cs:98` returns `ex.ToString()` over the wire; no try/catch around dispatch in `SiloMessagePump.cs:125` / `GatewayMessagePump`; `GrainIdleCollector.cs:39` unguarded | Wire errors are generic (full detail logged server-side only); per-connection and per-background-loop exception guards so one fault cannot drop connections or kill a service. |
| **A5** | AOT-correctness sweep | MEDIUM | `GatewayClientSubscription.cs:34-40` runtime `item.GetType()` for codec lookup; analyzers don't catch it | Stream codec path guarded or generic-typed; analyzer extended to flag `object.GetType()`→codec patterns; documented reflection audit of production packages. |

### Track B — TRUST (reliability & correctness, provable)

| ID | Title | Severity | Evidence | Exit criterion |
|----|-------|----------|----------|----------------|
| **B1** | Delivery-guarantee semantics + idempotency | HIGH | No dedup in `MessageDispatcher`; one-way is fire-and-forget (`MessageType.OneWayRequest`) | Documented per-message-type guarantees; request-response dedup cache (correlation-id → cached response, TTL); one-way ack option. |
| **B2** | Failover hygiene | HIGH | Stale grain-directory entries pruned only lazily on call failure (`LocalGrainCallInvoker`); no client reconnect/retry (`TcpGatewayConnection` faults all pending on drop); hardcoded membership timeouts (`MembershipOracle.cs:15-16`) | Proactive directory cleanup on silo death; client reconnect + bounded backoff retry; membership timeouts moved to `SiloRuntimeOptions`. |
| **B3** | Graceful drain on shutdown | MEDIUM | `SiloHostedService.StopAsync` disposes activation table with no drain timeout | Configurable drain timeout: stop accepting new calls, await in-flight, force-cancel after timeout, warn on long runners. |
| **B4** | Transaction 2PC completion | HIGH | `TransactionCoordinator.CommitAsync` commits writers sequentially with no prepare/abort; `[Transaction]` is metadata-only | Real prepare → commit/abort protocol, durable transaction log, abort-on-participant-failure, single-participant fast path. |
| **B5** | Reminder CAS + stream delivery mode | MEDIUM | `DefaultReminderService.FireDueRemindersAsync` advance-then-invoke double-fires under concurrent pollers; in-memory stream publish blocks on slow subscribers and throws aggregate | CAS/lease on reminder entries; defined stream delivery mode + non-blocking/backpressured fan-out. |
| **B6** | Test depth (the core trust deliverable) | HIGH | 216 tests, deterministic fault scenarios only; no property/chaos/partition/soak tests; CI runs tests un-gated | Fault+integration tests gated in CI; property-based (FsCheck) tests for scheduler/activation-table concurrency; deterministic-simulation + network-partition/split-brain tests; explicit concurrent-activation test; soak run. |

### Track C — FAST (performance, provable)

| ID | Title | Severity | Evidence | Exit criterion |
|----|-------|----------|----------|----------------|
| **C1** | Benchmark foundation | HIGH (unblocks all Fast claims) | **Zero** benchmark projects exist | `benchmarks/Quark.Benchmarks` (BenchmarkDotNet): micro (codec encode/decode, `IServiceScope` creation, activation-table lookup) + macro (local/remote call latency, throughput grains/sec, fan-out, stream publish). Baseline numbers committed. |
| **C2** | Allocation reduction | MEDIUM | `ArrayBufferWriter` per call in `GrainMessageSerializer`, `MessageSerializer`, `TcpGatewayCallInvoker`; `.ToArray()` copies; no `ArrayPool` anywhere; unconditional deep copy in `LocalGrainCallInvoker:99-100` | Pooled buffer writers across serializer + transport; eliminate `.ToArray()` copies; deep-copy short-circuit for value/immutable result types. Verified by C1 allocation benchmarks. |
| **C3** | Hot-path profiling & targeted optimization | MEDIUM | Per-call `IServiceScope` (`LocalGrainCallInvoker:92,155`), `ExecutionContext.Capture` (`GrainActivation:281`), per-call TCS (`GrainActivation:280`) all unquantified | Each cost quantified via C1; optimize only where measured (scope pooling / context-capture opt-out / TCS reuse) with before/after numbers. |
| **C4** | AOT startup & footprint benchmark | MEDIUM | "AOT-first" is positioned but never measured | Cold-start (start → first grain call) and working-set/RSS measured for AOT-published vs JIT; published as the headline differentiator. |
| **C5** | vs-Orleans comparative suite | MEDIUM | No Orleans comparison exists | Identical workloads run on Orleans + Quark (local call, remote call, throughput, GC pauses, cold start); results published with hardware/config methodology and honest caveats. |

### Track D — FOUNDATION (proof & process, cross-cutting)

| ID | Title | Severity | Evidence | Exit criterion |
|----|-------|----------|----------|----------------|
| **D1** | CI hardening | HIGH | `ci.yml` runs `dotnet test` un-gated; no AOT-publish gate, no benchmark regression gate, no security scan | CI stages: fault+integration tests, AOT publish (multi-OS), benchmark-regression gate, CodeQL/security scan, SBOM + package signing. |
| **D2** | Security & guarantees documentation | MEDIUM | No `SECURITY.md`, no threat model, no delivery-guarantee docs | `SECURITY.md` + threat model + published per-message-type delivery-guarantee table. |

## 4. Honest competitive positioning

- **Do not** claim "faster than Orleans" until C1/C5 show it on equivalent hardware.
- The AOT cold-start advantage (C4) is likely real and is the strongest near-term
  differentiator — but must be quantified before it is marketed.
- Track A criticals (A1–A3) are **show-stoppers for any networked `0.1.0`**: a young
  framework that OOMs on a malformed packet or lets any peer invoke any grain will not be
  trusted regardless of feature count. Closing them is the cheapest, highest-leverage trust
  win available.

## 5. Out of scope (explicit YAGNI)

- Built-in RBAC framework (an authN *SPI*, A3, is in scope; a full authZ policy engine is
  not).
- Connection pooling / multi-gateway client load-balancing (noted by audit, deferred —
  reconnect/retry in B2 is the priority).
- Durable/cross-silo stream providers and `[Transaction]` auto-coordination middleware
  (already tracked as separate Phase-6+ feature work, not hardening).

## 6. Execution model

Tracked as four **epic** issues (A/B/C/D), each with **child** issues per item, cross-linked
and grouped under a single milestone. Labels reuse the existing taxonomy (`area:*`, `type:*`,
`status:*`) plus new `severity:*`, `roadmap:*`, and `epic` labels. Recommended order:
A-criticals → C1 → D1 → parallel.
