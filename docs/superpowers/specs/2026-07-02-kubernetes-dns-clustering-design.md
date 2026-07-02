# Design: Kubernetes/DNS cluster discovery (Quark.Clustering.*)
**Issue:** #114
**Date:** 2026-07-02
**Status:** Draft — ready for implementation

---

## 1. Problem statement

`IMembershipTable` (`src/Quark.Core.Abstractions/Clustering/IMembershipTable.cs`) is already a pluggable
seam, but the only implementation is `InMemoryMembershipTable`, wired exclusively by
`UseLocalhostClustering()`. Real deployments — Kubernetes especially, given Quark's AOT/container
posture — need silos and clients to find each other with no static endpoint list.

Issue #114 asks for "Kubernetes/DNS-based cluster discovery." Studying the live code exposes a truth
the issue title understates: **DNS/k8s is a read-only discovery mechanism, not a writable membership
table**, and — more importantly — **Quark has no cross-process clustering today at all**. Any honest
design for #114 must confront that gap rather than paper a DNS provider over a runtime that cannot use
it.

### What the code actually does today (verified)

- **`IMembershipTable`** — 4 methods (`ReadAllAsync`, `InsertRowAsync`, `UpdateRowAsync`,
  `UpdateIAmAliveAsync`). Each silo writes its own row; the oracle reads all rows. Only
  `InMemoryMembershipTable` exists; it stores rows in a process-local `ConcurrentDictionary`.
- **`MembershipOracle`** (`BackgroundService`) — inserts self as `Active`, writes `IAmAlive` every
  10 s (`IAmAliveInterval`, hardcoded static), evicts peers past 30 s (`DeadSiloThreshold`, hardcoded
  static) by setting `Status = Dead` and calling `_router.Unregister(...)`. Marks self `Dead` on
  shutdown.
- **`ISiloRouter`** — maps `SiloAddress → IGrainCallInvoker`. The **only** implementation is
  `InProcessSiloRouter`, and `SiloHostedService` registers **the local silo's own
  `LocalGrainCallInvoker`** into it. There is **no TCP silo-to-silo invoker** — the only
  `IGrainCallInvoker` implementations are `LocalGrainCallInvoker`, `TcpGatewayCallInvoker`
  (client→gateway), and `LocalObserverCallInvoker`. Cross-silo routing therefore only works when all
  silos live in one OS process (`SharedLocalhostCluster` keyed by `clusterId:serviceId`).
- **`SiloMessagePump`** listens on the silo port and dispatches inbound messages to the local invoker,
  but nothing **dials** a peer silo — the receive half exists, the connect half does not.
- **Client gateway discovery** — `TcpGatewayClientOptions.GatewayEndpoint` is a **single**
  `IPEndPoint`. `TcpGatewayClusterClient.Connect` opens exactly one connection to it. There is no
  gateway list, no refresh, no failover to a second gateway.

**Conclusion:** the InMemory membership table + InProcess router is an in-process simulation of a
cluster. A DNS/k8s provider is not a drop-in swap into a working distributed runtime; the distributed
runtime does not exist yet. This spec is written honestly around that fact.

---

## 2. Goals / Non-goals

### Goals
- Ship a **real, writable, cross-process membership table** so silos in separate pods share one source
  of truth. Recommended first implementation: **`Quark.Clustering.Redis`** (mirrors the existing
  `Quark.Persistence.Redis` / `Quark.Reminders.Redis` precedent and conventions).
- Ship **DNS-based discovery for Kubernetes** (`Quark.Clustering.Kubernetes`) that resolves a
  **headless service** to find peer silos and gateway pods, using **only `System.Net.Dns`** — no
  `KubernetesClient` NuGet, no k8s API server dependency.
- Introduce a **client-side `IGatewayListProvider`** abstraction so a client can connect to any live
  gateway among N pods and refresh the list as pods come and go. This is the **immediately shippable,
  independently valuable** slice: a client hitting a k8s headless service works today because the
  gateway pump + client protocol already exist.
- Keep membership liveness on **one** model (heartbeat oracle) rather than forking it per provider.
- Make membership timeouts configurable (currently hardcoded) — coordinated with, not duplicated from,
  #60.

### Non-goals (explicit, load-bearing)
- **Silo-to-silo TCP transport / networked `ISiloRouter`.** This is the true prerequisite for real
  multi-pod grain placement and is **out of scope for #114** — it deserves its own issue (see §11).
  #114 populates membership and discovery; a networked router is what *consumes* peer addresses to
  route a call to a grain on another pod. Without it, a Redis/DNS-populated membership table gives
  correct *visibility* of peers but grain calls still only resolve locally. This spec ships the
  discovery + table + client gateway list and names the router work as a hard dependency.
- Client reconnect/backoff and proactive directory cleanup on silo death — owned by **#60**. This
  spec defines the seams (`IGatewayListProvider` refresh, oracle death detection) that #60 builds on;
  it does not implement retry/backoff.
- A Kubernetes Operator, CRDs, or Endpoints/EndpointSlice **API watch**. DNS polling of a headless
  service is sufficient and far cheaper on the AOT/trim budget (see §7).
- Non-Redis writable stores (etcd, Consul, ADO). Future providers can follow the same seam.

---

## 3. Architecture decision: table vs. discovery

The task framing — "(a) a real shared writable table, plus (b) DNS/k8s as seed/discovery" — is the
correct decomposition. The decision is **how DNS relates to the table**. Three options were considered:

| Option | What DNS is | Liveness authority | Verdict |
|---|---|---|---|
| **A. DNS-as-table** (`DnsMembershipTable : IMembershipTable`) | The whole membership list — `ReadAllAsync` = resolve headless service A-records; writes are no-ops | Kubernetes readiness probes (k8s drops non-ready pods from Endpoints → they vanish from DNS) | Viable for pure-k8s, **but** breaks the oracle contract (Insert throws / Update no-op), can't express `Joining`/`ShuttingDown`/`Generation`, and forks liveness into a second model. **Deferred to a follow-up**, not v1. |
| **B. DNS-as-seed + writable table** | A bootstrap hint that finds initial peers; those peers self-register into a real table | The heartbeat oracle over the writable table | **Recommended.** One liveness model, honest table semantics, works in and out of k8s. |
| **C. k8s Endpoints API watch** | Live push of ready pods from the API server | Kubernetes | Rejected for v1: pulls in `KubernetesClient` (reflection/JSON-reflection, trim-hostile — see §7) and an API-server dependency + RBAC for something DNS already answers. |

**Recommendation: Option B, split across two packages, sequenced.**

1. **`Quark.Clustering.Redis` first** — the real writable table. It is the honest unblock: it makes
   membership work across processes *anywhere* (VMs, Docker Compose, k8s), and it reuses Quark's
   established Redis conventions. A client `RedisGatewayListProvider` falls out of it for free (read
   `Active` rows' `GatewayAddress`).
2. **`Quark.Clustering.Kubernetes` second** — DNS discovery that layers on top: a `DnsGatewayListProvider`
   (client resolves the headless service, each ready pod = a gateway) and a `DnsSeedProvider` (silos
   resolve peers at startup to bootstrap, and/or to run Redis-free in the DNS-as-table follow-up).

Justification for Redis-first: without a writable cross-process table, the k8s package would have to be
Option A (DNS-as-table) to do anything at all — forcing the liveness fork we want to avoid — or it
would have nothing to seed *into*. Redis-first keeps liveness singular and gives the k8s package a real
target. It also matches the repo's demonstrated pattern (InMemory → Redis) for persistence and
reminders, so it is the least surprising shape for maintainers.

---

## 4. Proposed API & packages

### 4.1 Shared abstractions (add to `Quark.Core.Abstractions.Clustering`)

`IMembershipTable` already lives in `Quark.Core.Abstractions` and is referenced by both silo and client
sides — no new abstractions package is warranted. Add the client-side discovery contract alongside it.

```csharp
namespace Quark.Core.Abstractions.Clustering;

/// <summary>
///     Client-side source of live gateway endpoints. Replaces the single fixed
///     TcpGatewayClientOptions.GatewayEndpoint with a refreshable list.
/// </summary>
public interface IGatewayListProvider
{
    /// <summary>Returns the currently known set of reachable gateway addresses.</summary>
    ValueTask<IReadOnlyList<SiloAddress>> GetGatewaysAsync(CancellationToken ct = default);

    /// <summary>How often the client should re-query this provider. Zero = static list.</summary>
    TimeSpan RefreshPeriod { get; }

    /// <summary>True when the list can change at runtime (DNS, Redis) vs. fixed (localhost).</summary>
    bool IsUpdatable { get; }
}

/// <summary>
///     Silo-side bootstrap hint: resolves an initial set of peer silo addresses so a joining
///     silo can discover the cluster before/independently of the writable membership table.
/// </summary>
public interface IMembershipSeedProvider
{
    ValueTask<IReadOnlyList<SiloAddress>> GetSeedsAsync(CancellationToken ct = default);
}
```

`MembershipEntry` gains no new required fields, but the Redis table depends on `GatewayAddress` being
recorded per silo. Today `SiloRuntimeOptions.GatewayAddress` exists but `MembershipEntry` does not carry
it. **Add** `SiloAddress? GatewayAddress` (nullable, non-breaking) to `MembershipEntry` so a client
gateway-list provider can derive gateways from the table.

### 4.2 `Quark.Clustering.Redis` (new package)

References: `Quark.Core.Abstractions`, `StackExchange.Redis`, MEDI/Options. Targets `net9.0;net10.0`.
Mirrors `Quark.Persistence.Redis` project shape exactly.

```csharp
namespace Quark.Clustering.Redis;

public sealed class RedisClusteringOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string ClusterId { get; set; } = "dev";      // key namespace
    public string ServiceId { get; set; } = "QuarkService";
}

public sealed class RedisMembershipTable : IMembershipTable { /* Hash per cluster: field=SiloAddress, value=serialized MembershipEntry */ }

public sealed class RedisGatewayListProvider : IGatewayListProvider { /* reads Active rows, projects GatewayAddress */ }

public static class RedisClusteringServiceCollectionExtensions
{
    // Silo side
    public static ISiloBuilder UseRedisClustering(this ISiloBuilder builder, Action<RedisClusteringOptions> configure);
    // Client side
    public static IClientBuilder UseRedisClustering(this IClientBuilder builder, Action<RedisClusteringOptions> configure);
}
```

Concurrency note: `UpdateRowAsync` marking a peer `Dead` may race between multiple silos. Redis makes
this safe with a scripted/atomic compare-and-set on the entry; the last writer wins and the result is
idempotent (Dead is terminal). No distributed lock needed.

### 4.3 `Quark.Clustering.Kubernetes` (new package)

References: `Quark.Core.Abstractions` + BCL only (`System.Net.Dns`). **No `KubernetesClient`, no
`DnsClient.NET`.** Targets `net9.0;net10.0`.

```csharp
namespace Quark.Clustering.Kubernetes;

public sealed class KubernetesDnsOptions
{
    /// <summary>Headless service DNS name, e.g. "quark-silo.default.svc.cluster.local".</summary>
    public required string HeadlessServiceName { get; set; }
    /// <summary>Gateway port shared by all pods (containerPort). Fixed across the Deployment/StatefulSet.</summary>
    public int GatewayPort { get; set; } = 30000;
    /// <summary>Silo port shared by all pods.</summary>
    public int SiloPort { get; set; } = 11111;
    public TimeSpan RefreshPeriod { get; set; } = TimeSpan.FromSeconds(10);
}

public sealed class DnsGatewayListProvider : IGatewayListProvider { /* Dns.GetHostAddresses(HeadlessServiceName) → [ip:GatewayPort] */ }

public sealed class DnsSeedProvider : IMembershipSeedProvider { /* Dns.GetHostAddresses(...) → [ip:SiloPort] */ }

public static class KubernetesClusteringExtensions
{
    // Client: discover gateways via the headless service
    public static IClientBuilder UseKubernetesGatewayDiscovery(this IClientBuilder builder, Action<KubernetesDnsOptions> configure);
    // Silo: seed cluster bootstrap via the headless service (writes into the configured writable table, e.g. Redis)
    public static ISiloBuilder UseKubernetesSeeding(this ISiloBuilder builder, Action<KubernetesDnsOptions> configure);
}
```

**Why A-records, not SRV:** every pod in a Deployment/StatefulSet exposes the same fixed
`containerPort`, so a single A/AAAA resolution of the headless service name yields every pod IP and the
port is known from config. SRV (which `System.Net.Dns` does **not** support without a third-party
client) is only needed for per-instance ports, which Quark pods do not have. This keeps the package
BCL-only and AOT-clean.

### 4.4 Compatibility tier

| Surface | Tier | Justification |
|---|---|---|
| `IGatewayListProvider` | **Quark-native** | Orleans' equivalent is `IGatewayListProvider` (same name/intent) but a different signature (`IList<Uri> GetGateways()`). Concept is drop-in; shape is Quark-native (`SiloAddress`, `ValueTask`, refresh metadata). |
| `UseRedisClustering()` | **minor-change** | Orleans has `UseRedisClustering(...)`; same name, Quark DI wiring and options. |
| `UseKubernetesGatewayDiscovery()` / `UseKubernetesSeeding()` | **Quark-native** | Orleans' k8s hosting uses the Endpoints API + `KubernetesClient`; Quark deliberately diverges to DNS-only. Different name signals different semantics. |
| `IMembershipTable`, `MembershipEntry.GatewayAddress` | **drop-in / additive** | Interface unchanged; one nullable field added. |

---

## 5. Runtime integration (anchors)

Ordered by file, safe top-to-bottom.

1. **`src/Quark.Core.Abstractions/Clustering/MembershipEntry.cs`** — add nullable `GatewayAddress`.
2. **`src/Quark.Core.Abstractions/Clustering/IGatewayListProvider.cs`** (new) + `IMembershipSeedProvider.cs` (new).
3. **`src/Quark.Runtime/Clustering/MembershipOracle.cs`** — replace hardcoded `IAmAliveInterval` /
   `DeadSiloThreshold` statics with reads from `SiloRuntimeOptions` (new
   `IAmAliveInterval`/`DeadSiloThreshold`/`SiloStatus` options; keep current values as defaults). Record
   `GatewayAddress` in the self entry it inserts. **Coordinate with #60** — that issue also wants these
   timeouts configurable; this spec adds the option fields, #60 documents/tunes and adds the proactive
   directory prune on death. Do not implement #60's directory cleanup here.
4. **`src/Quark.Runtime/SiloHostedService.cs`** — no change required for the table itself. **Flag**:
   the `router.Register(_options.SiloAddress, invoker)` call registers the *local* invoker; the
   networked-router prerequisite (§11) is where peer registration would hook in.
5. **`Quark.Clustering.Redis`** package — `RedisMembershipTable`, `RedisGatewayListProvider`, DI ext.
   `UseRedisClustering()` on `ISiloBuilder` registers the table + `MembershipOracle` +
   `GatewayMessagePump` (the pieces `UseLocalhostClustering` wires today, minus the in-process shared
   state) and configures `SiloRuntimeOptions` from real host env (pod IP / ports), not loopback.
6. **`src/Quark.Client.Tcp/TcpGatewayClientOptions.cs`** + **`TcpGatewayClusterClient.cs`** — introduce
   an `IGatewayListProvider` seam. Default provider wraps the existing single `GatewayEndpoint`
   (back-compat: `UseLocalhostGateway`/`UseTcpGateway` register a static one-item provider with
   `IsUpdatable=false`). `Connect` picks one gateway (random/round-robin); on connect failure it tries
   the next. Reconnect/backoff loop is **#60's** job — expose the hook, don't implement the policy.
7. **`Quark.Clustering.Kubernetes`** package — `DnsGatewayListProvider` (client),
   `DnsSeedProvider` (silo), DI ext.

Directory.Packages.props already has `StackExchange.Redis` and `Testcontainers.Redis`; no new package
versions needed for Redis. Kubernetes package needs none.

`Quark.slnx` + `Quark.Server` meta-package: `Quark.Clustering.Redis`/`.Kubernetes` are opt-in provider
packages (like `Quark.Persistence.Redis`) — **not** folded into `Quark.Server`.

---

## 6. Kubernetes specifics

- **Headless service** (`clusterIP: None`) fronting the silo `Deployment`/`StatefulSet`. Its DNS name
  resolves to the set of ready pod IPs (A/AAAA). Client resolves it for gateways; a joining silo
  resolves it for seed peers.
- **Readiness/liveness probes** are Kubernetes' concern. A pod failing readiness is dropped from the
  headless service endpoints, so DNS naturally stops advertising it — this is the DNS-side liveness
  signal (used by the client gateway list). The *authoritative* silo liveness for grain routing remains
  the heartbeat oracle over the writable (Redis) table, so a pod that is DNS-visible but heartbeat-stale
  is still marked `Dead`.
- **No k8s API dependency, no RBAC, no ServiceAccount token.** DNS resolution needs nothing beyond
  cluster DNS (CoreDNS), which every cluster provides. This is a deliberate divergence from Orleans'
  `Microsoft.Orleans.Clustering.Kubernetes` (which watches the Endpoints API).
- **Ports** are fixed containerPorts shared by all pods → A-records + known port suffice (no SRV).

### Rolling restart / liveness / who declares death

- **Who declares a silo dead:** the `MembershipOracle` on *peer* silos, when a row's `IAmAlive` is
  staler than `DeadSiloThreshold`. On graceful shutdown a silo marks itself `Dead`
  (`MarkSelfDeadAsync`). k8s readiness removal is an *additional*, faster hint for the client gateway
  list but never the sole authority for grain routing.
- **Rolling restart:** k8s terminates old pods while starting new ones. New pods get **new IPs** →
  new `MembershipEntry` rows. IP reuse across generations is disambiguated by
  `SiloAddress.Generation` (already present). Terminating pods transition `Active → ShuttingDown →
  Dead`; the client, seeing DNS drop the terminating pod and/or the Redis row flip to non-`Active`,
  stops routing new connections there. Because grace period > oracle heartbeat interval by
  configuration guidance, peers observe the graceful `Dead` before the timeout eviction fires — avoiding
  a false-positive during normal rollout.
- **Split-membership risk (DNS-as-table follow-up only):** if DNS were the table (Option A), two
  generations briefly sharing an IP during rollout could confuse membership. The generation counter
  mitigates it; Option B avoids it entirely because Redis rows are keyed by full `SiloAddress`
  (host+port+generation). Another reason B is v1.

---

## 7. AOT & trim notes

- **`KubernetesClient` NuGet is out.** It relies on reflection-based JSON (`System.Text.Json` without
  source-gen context in its models, plus `Newtonsoft` paths) and dynamic model binding — trim-hostile
  and would trip `EnableAotAnalyzer` warnings the repo treats as errors (`Directory.Build.props`).
  Avoiding it is a primary design driver, not an afterthought.
- **`System.Net.Dns.GetHostAddresses` is AOT/trim-safe** — no reflection, no dynamic codegen. This is
  the entire runtime surface of `Quark.Clustering.Kubernetes`.
- **No SRV** → no `DnsClient.NET` third-party dependency (it is reflection-light but still an avoidable
  dependency; A-records make it unnecessary).
- **`StackExchange.Redis`** is already used AOT-acceptably by `Quark.Persistence.Redis` /
  `Quark.Reminders.Redis`; `RedisMembershipTable` follows the same patterns (`ConnectionMultiplexer`,
  `RedisValue` byte payloads). `MembershipEntry` serialization uses the existing
  `[GenerateSerializer]` source-gen path or a hand-written codec — **no reflection JSON**.
- Both new packages set `IsTrimmable=true` / `EnableAotAnalyzer=true` via `Directory.Build.props`; no
  `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` annotations should be needed. If any appear,
  that is a signal the design drifted.
- Explicit registration only — no assembly scanning. Providers are opted in via `UseRedisClustering()`
  / `UseKubernetes*()`.

---

## 8. Test plan

- **Unit — `RedisMembershipTable`** (Testcontainers Redis, `[Trait("category","integration")]`,
  skip when unavailable): insert/read/update/IAmAlive round-trips; duplicate insert throws; concurrent
  Dead-marking is idempotent; `GatewayAddress` survives round-trip.
- **Unit — `DnsGatewayListProvider` / `DnsSeedProvider`**: inject a resolver seam
  (`Func<string, ValueTask<IReadOnlyList<IPAddress>>>`) so tests avoid real DNS — verify A-records map
  to `[ip:port]`, empty result handled, refresh period honored. **Do not** depend on live DNS in CI.
- **Unit — `IGatewayListProvider` client integration**: static provider preserves back-compat
  (`UseLocalhostGateway` still connects); multi-gateway provider picks a live one and skips a dead one
  on connect failure (failover *selection*, not #60's reconnect policy).
- **Integration — cross-process membership over Redis** (`Quark.Tests.Integration`): two `TestSilo`
  hosts pointed at one Redis, distinct `SiloAddress`; both appear in `ReadAllAsync`; kill one → the
  survivor's oracle flips it to `Dead` within `DeadSiloThreshold`; graceful stop marks self `Dead`
  immediately.
- **Rolling-restart simulation**: register gen N and gen N+1 rows for the same host:port; verify they
  are distinct entries and the client prefers `Active`.
- **AOT smoke**: `dotnet publish ... /p:PublishAot=true` on a tiny host that references both new
  packages — must produce zero trim/AOT warnings.
- **Note (memory):** two pre-existing timing-flaky oracle-adjacent unit tests fail only under parallel
  load; keep new timing tests tolerant / run oracle timeout tests with generous margins.

---

## 9. Implementation checklist (ordered, no circular deps)

1. `MembershipEntry.GatewayAddress` (nullable) — `Quark.Core.Abstractions`.
2. `IGatewayListProvider`, `IMembershipSeedProvider` — `Quark.Core.Abstractions.Clustering`.
3. `SiloRuntimeOptions`: add `IAmAliveInterval`, `DeadSiloThreshold` (defaults = current statics).
4. `MembershipOracle`: read timeouts from options; stamp `GatewayAddress` on self entry.
5. New project `src/Quark.Clustering.Redis` (copy `Quark.Persistence.Redis` csproj shape):
   `RedisClusteringOptions`, `RedisMembershipTable`, `RedisGatewayListProvider`,
   `RedisClusteringServiceCollectionExtensions` (silo + client overloads).
6. Client seam: `TcpGatewayClientOptions` gains provider hookup; default static
   `IGatewayListProvider`; `TcpGatewayClusterClient.Connect` selects from the provider (failover
   selection only — leave a clear TODO/hook for #60 reconnect/backoff).
7. New project `src/Quark.Clustering.Kubernetes`: `KubernetesDnsOptions`,
   resolver-seam-based `DnsGatewayListProvider` + `DnsSeedProvider`, `KubernetesClusteringExtensions`.
8. Register both projects in `Quark.slnx`; add to CI test matrix.
9. Tests per §8.
10. Wiki: extend `Clustering-and-Transport.md` + a new "Kubernetes deployment" section; update
    `FEATURES.md` parity tracker.
11. A minimal end-to-end sample (optional, follow-up): `samples/Cluster` with a headless-service YAML.

---

## 10. Resolved design decisions

1. **Redis table first, k8s DNS second** — one liveness model; DNS does discovery, not liveness-of-record.
2. **DNS A-records, not SRV, not the Endpoints API** — BCL-only, AOT-clean, no RBAC.
3. **`KubernetesClient` NuGet rejected** — trim-hostile; not worth what DNS already answers.
4. **`IGatewayListProvider` is the client-side unblock** — shippable now atop the existing gateway pump.
5. **New abstractions live in `Quark.Core.Abstractions.Clustering`**, no new `*.Abstractions` package —
   follows the precedent that `IMembershipTable` already lives there and both client and runtime
   reference it.
6. **`Quark.Clustering.*` are opt-in provider packages**, not part of `Quark.Server`.
7. **Membership timeouts move to `SiloRuntimeOptions`** — additive; #60 tunes/documents them.

---

## 11. Dependencies & related work

- **HARD PREREQUISITE (own issue, not #114): networked `ISiloRouter` + silo-to-silo TCP invoker.**
  Today `InProcessSiloRouter` only holds in-process invokers and there is no TCP peer invoker. A
  Redis/DNS-populated membership table gives correct peer *visibility*, but a grain call still cannot
  cross a pod boundary until a router dials the peer `SiloAddress` and produces an `IGrainCallInvoker`
  over TCP (analogous to `TcpGatewayCallInvoker`, but silo→silo). **#114's discovery + table are
  necessary but not sufficient for real multi-pod grain placement.** The independently valuable slice
  #114 *can* ship without the router is **client gateway discovery** (`IGatewayListProvider` +
  `Quark.Clustering.Kubernetes` client side) — a client connecting to any live gateway pod works with
  today's runtime. Recommend filing the networked-router issue and sequencing it before the "cluster of
  silos across pods" claim is made in docs.
- **#60 (B2 — failover hygiene, planned):** owns proactive directory cleanup on silo death, client
  reconnect with bounded backoff + idempotent retry, and making membership timeouts configurable. This
  spec adds the timeout **option fields** and the `IGatewayListProvider` **refresh seam** #60 relies on,
  and defers all retry/backoff/cleanup policy to it. Do not duplicate that work here.
- **Precedent:** `Quark.Persistence.Redis`, `Quark.Reminders.Redis` (InMemory → Redis split, DI
  conventions, Testcontainers usage) — mirror their project shape and options patterns.
- **Cross-system references (from the issue):** akkadotnet/akka.net#1642 (cluster discovery, shipped),
  Orleans `Microsoft.Orleans.Clustering.Kubernetes` (Endpoints API — the approach Quark deliberately
  does *not* take).

---

## 12. Open questions

1. **Networked router scope** — file as a separate prerequisite issue (recommended), or fold a minimal
   TCP peer invoker into #114? This determines whether #114 delivers "multi-pod silos" or only
   "multi-pod client gateway discovery + shared membership visibility."
2. **DNS-as-table (Option A) as a Redis-free path** — worth a follow-up for pure-k8s users who don't
   want Redis? It requires the oracle to support a "delegated liveness / read-only table" mode.
3. **Gateway selection policy** in `TcpGatewayClusterClient` — random vs. round-robin vs.
   least-connections? Random is simplest and adequate for v1; confirm.
4. **Redis key schema & TTL** — should dead rows self-expire via Redis TTL (cheap GC) in addition to the
   oracle marking them `Dead`? Proposed: yes, TTL ≫ `DeadSiloThreshold` as a backstop.
5. **`MembershipEntry` serialization** — reuse `[GenerateSerializer]` codecs, or a purpose-built compact
   Redis encoding? Leaning source-gen codec for consistency.
