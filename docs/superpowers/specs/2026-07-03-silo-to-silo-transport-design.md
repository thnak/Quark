# Design: Silo-to-silo transport — networked `ISiloRouter` + cross-silo grain call invoker
**Issue:** #126
**Date:** 2026-07-03
**Status:** Draft — ready for implementation
**Prerequisite-of:** #114 (multi-pod placement), #39 (IManagementGrain cross-machine fan-out)

---

## 1. Problem statement

Quark has **no cross-process grain call path**. A membership table can make peers *visible*
(the 2026-07-02 Kubernetes/DNS spec ships Redis/DNS discovery), but a grain call still cannot cross a
pod/process boundary. This issue delivers the transport that consumes a peer `SiloAddress` and actually
routes a call there.

The k8s-clustering spec (§11) named this as its hard prerequisite and deferred it deliberately. This
spec honours that hand-off and does the code archaeology the issue framing understated: the gap is
**not one seam but two**, and the second was not obvious from the issue title.

### What the code actually does today (verified against live source)

- **`ISiloRouter`** (`src/Quark.Runtime/Clustering/ISiloRouter.cs`) — `Register(SiloAddress, IGrainCallInvoker)`,
  `Unregister(SiloAddress)`, `bool TryGetInvoker(SiloAddress, out IGrainCallInvoker)`. The **only**
  implementation is `InProcessSiloRouter` — a bare `ConcurrentDictionary<SiloAddress, IGrainCallInvoker>`
  (`InProcessSiloRouter.cs:14`). It is a map, nothing more; it never dials.
- **`SiloHostedService`** registers the silo's **own** `LocalGrainCallInvoker` into the router under its
  own address (`SiloHostedService.cs:60-64`) and `Unregister`s on stop (`:76-77`). No peer is ever
  registered.
- **`LocalhostClusterState`** (`Clustering/LocalhostClusterState.cs`) shares **one**
  `InMemoryGrainDirectory` + **one** `InProcessSiloRouter` + one `InMemoryMembershipTable` across all
  silos in the process. This is why in-process multi-silo "works": the router map and the directory are
  literally the same objects for every silo. Cross-process, each silo has its own map and its own
  directory, shared with nobody.
- **The remote-routing seam** is `LocalGrainCallInvoker.TryRouteRemote(grainId)`
  (`LocalGrainCallInvoker.cs:231-255`):
  1. `_siloRouter is null` → `null` (activate local).
  2. `!_directory.TryLookup(grainId, out owner)` → `null` (activate local). **← the miss falls local.**
  3. `owner == _siloAddress` → `null` (local).
  4. `_siloRouter.TryGetInvoker(owner, out remote)` → route to `remote`.
  5. else → `_directory.TryUnregister(grainId, owner)` → `null` (stale entry, activate local).
- **`PlacementDirector.SelectActivationSilo(...)` is never called on the activation path.** `grep`
  confirms the only callers are unit/integration **tests**. The live activation path is
  `LocalGrainCallInvoker.CreateActivationAsync` (`:271-323`), which **unconditionally activates the grain
  locally** and then `_directory.TryRegister(grainId, _siloAddress)` (`:310`). Placement strategy is
  fully implemented but **dead-wired** into activation.
- **The client→silo TCP path is the reuse target.** `TcpGatewayCallInvoker`
  (`Quark.Client.Tcp/TcpGatewayCallInvoker.cs`) serializes an invokable into a `GrainInvocationRequest`,
  wraps it in a `MessageEnvelope { MessageType.Request }`, sends it over a `TcpGatewayConnection`
  (correlation-id multiplex, `SendAndAwaitAsync`), and reads back a `GrainInvocationResponse`. The
  receive half — `SiloMessagePump.ProcessConnectionAsync` → `MessageDispatcher.DispatchAsync` →
  `TransportGrainDispatcherRegistry.GetDispatcher(type)` → the local `IGrainCallInvoker` — is already
  silo-side and already shared by `GatewayMessagePump`. **The receive half is done; only the silo-side
  connect + invoker half is missing.**

### Conclusion — the two gaps

| Gap | What it is | Consequence if unfixed |
|---|---|---|
| **G1 — transport** (the issue's stated scope) | No `IGrainCallInvoker` that dials a peer `SiloAddress` over TCP; `ISiloRouter.TryGetInvoker` can only ever return an in-process invoker. | Even when the directory names a remote owner, the router has no invoker → `TryUnregister` + local. |
| **G2 — placement never runs** (surfaced by archaeology, **not** in the issue title) | `PlacementDirector` is disconnected from `CreateActivationAsync`. A directory *miss* always activates locally. Grains land wherever the first call happens to hit. | Even with G1 fixed, a fresh grain never routes remotely, because nothing consults placement to name a remote owner on the miss path. |

Fixing G1 alone (a networked router) satisfies the literal issue text but changes **nothing observable**:
the miss path still activates locally, so `TryRouteRemote` never sees a remote `owner` for a
not-yet-placed grain. **Both gaps must close for a call to cross a boundary.** This spec closes both and
is explicit about the residual correctness limit (single-activation under *non-deterministic* placement)
that a distributed directory — a named follow-up — must close.

---

## 2. Goals / Non-goals

### Goals
- A **networked `ISiloRouter`** whose `TryGetInvoker` returns a `SiloCallInvoker` that dials a peer
  `SiloAddress` over the existing `ITransport` and multiplexes calls over one pooled connection —
  the silo→silo analogue of `TcpGatewayCallInvoker`/`TcpGatewayConnection`.
- **Wire `PlacementDirector` into the activation miss path** so a call for an unplaced grain consults
  placement against the live active-silo set and, when placement returns a remote silo, routes there
  instead of activating locally.
- **Single-activation correctness for deterministic placement** (`[HashBasedPlacement]`) across
  processes with the existing per-silo `InMemoryGrainDirectory` — because every silo independently
  computes the same owner, per-process directories stay coherent.
- **Wire-protocol reuse:** `MessageEnvelope` / `GrainInvocationRequest` / `MessageDispatcher` /
  `TransportGrainDispatcherRegistry` unchanged except for **one additive loop-guard marker** (§7).
- **Connection lifecycle** owned here: lazy dial-on-first-call, one pooled multiplexed connection per
  peer, teardown driven by membership death. Reconnect **policy** (bounded backoff, retry-safe replay)
  is explicitly **#60's**; this spec names the seam.
- **TLS/auth reuse:** silo-to-silo links ride the same `TcpTransportOptions.Tls` and the #56
  `IConnectionAuthenticator` SPI on both dial and accept. No parallel auth mechanism.
- AOT/trim-clean: explicit registration, no reflection, reuse the already-AOT-clean codec/serializer
  path.

### Non-goals (explicit, load-bearing)
- **Distributed / cluster grain directory.** Single-activation under **non-deterministic** placement
  (`[RandomPlacement]`, `[PreferLocalPlacement]`, `[StatelessWorker]`) across processes requires a
  directory that peers share. Per-process directories will let two silos independently place the same
  random-placed grain → **double activation**. This spec ships correct deterministic (hash-based)
  cross-silo placement and **documents** the non-deterministic limit. Recommend a follow-up issue:
  *distributed grain directory (`IGrainDirectory` over Redis / a directory-partition ring)*. (§6, §12-Q1.)
- **Reconnect/backoff policy and retry-safe replay** — **#60**. This spec faults pending calls on a
  dropped peer link and permits the next call to re-dial once; bounded exponential backoff and idempotent
  replay are #60's.
- **Proactive directory pruning on silo death** — **#60**. This spec's `PeerConnectionManager` unregisters
  the *router* entry (stops routing) on death; pruning *directory* entries cluster-wide is #60.
- **The authenticator SPI itself, fail-secure `AllowAny`, hostname/SAN validation** — **#56**. This spec
  consumes that SPI symmetrically on the peer link; it does not build it.
- **Silo-to-silo streaming / observer push fan-out across silos.** Only the request/response and one-way
  grain-call paths cross silos here. Cross-silo stream delivery is a separate concern.
- **Changing the `ISiloRouter` public shape.** The sync `TryGetInvoker(out)` contract is kept; lazy async
  dial lives *inside* the returned invoker, not in the router.

---

## 3. Architecture overview

```
Client → gateway silo A ──┐
                          │  A.MessageDispatcher → A.LocalGrainCallInvoker.InvokeAsync
                          │     ├─ TryRouteRemote(G): directory hit → route (fast path)
                          │     └─ directory MISS → PlacementDirector.SelectActivationSilo(G, activeSilos)
                          │            ├─ == self  → activate locally (as today)
                          │            └─ == silo B → route via ISiloRouter.TryGetInvoker(B)
                          ▼
                 NetworkedSiloRouter.TryGetInvoker(B) → SiloCallInvoker(B)
                          │  serialize invokable → MessageEnvelope{Request, hdr: x-quark-hop=1}
                          ▼
                 SiloPeerConnection(B)  ── TCP (ITransport.ConnectAsync, pooled, multiplexed) ──►
                          ▼
   silo B: SiloMessagePump → MessageDispatcher (sees x-quark-hop) → LOCAL-TERMINAL invoker
                          │     (bypasses TryRouteRemote → never re-forwards → no loop)
                          ▼
                 B.LocalGrainCallInvoker activates G locally, B.directory registers G→B
                          ▼
                 GrainInvocationResponse ◄── back over the same correlation id
```

Key composition insight: **the router stays a map.** The "networked" behaviour is entirely in the
*invoker values* (`SiloCallInvoker`) installed into that map by a `PeerConnectionManager` driven by
membership. `TryGetInvoker` remains a pure, synchronous dictionary lookup; the TCP dial happens lazily
*inside* `SiloCallInvoker` on first invoke. This preserves the existing `ISiloRouter` contract and the
`TryRouteRemote` "miss → `TryUnregister`" semantics unchanged.

---

## 4. API surface

### 4.1 `SiloCallInvoker` — the cross-silo invoker (new, `Quark.Runtime`)

Mirror of `TcpGatewayCallInvoker`, but silo-side and transport-abstraction-based. Lives in
`Quark.Runtime` (silo-side; may reference `Quark.Transport.Abstractions`, already used by
`SiloMessagePump`). It must **not** reference `Quark.Client.Tcp`.

```csharp
namespace Quark.Runtime.Clustering;

/// <summary>
///     IGrainCallInvoker that routes a grain call to a peer silo over a pooled, multiplexed
///     TCP connection. The silo-to-silo analogue of TcpGatewayCallInvoker. Dials lazily on first use.
/// </summary>
public sealed class SiloCallInvoker : IGrainCallInvoker
{
    public SiloCallInvoker(
        SiloAddress peer,
        SiloPeerConnection connection,          // owned/pooled by PeerConnectionManager
        GrainMessageSerializer grainSerializer);

    public ValueTask<TResult> InvokeAsync<TInvokable, TResult>(
        GrainId grainId, TInvokable invokable, CancellationToken ct = default)
        where TInvokable : struct, IGrainInvokable<TResult>;

    public ValueTask InvokeVoidAsync<TInvokable>(
        GrainId grainId, TInvokable invokable, CancellationToken ct = default)
        where TInvokable : struct, IGrainVoidInvokable;

    // Cross-silo observer push is a non-goal; throw NotSupportedException (mirrors gateway constraints).
    public ValueTask InvokeObserverAsync<TInvokable>(
        GrainId grainId, TInvokable invokable, CancellationToken ct = default)
        where TInvokable : struct, IObserverVoidInvokable;
}
```

Behaviour is byte-for-byte the `TcpGatewayCallInvoker` body (serialize invokable → `GrainInvocationRequest`
→ `MessageEnvelope{Request}` → `SendAndAwaitAsync` → `DeserializeResponse` → `DeserializeResult`), with
**one addition**: it stamps the loop-guard header (§7) on the outbound envelope.

### 4.2 `SiloPeerConnection` — pooled multiplexed peer link (new, `Quark.Runtime`)

Structural mirror of `TcpGatewayConnection` (correlation-id pending map, single-writer lock, read loop,
`FaultAllPending` on drop), but built on the injected `ITransport` rather than the concrete
`TcpTransport`, and **without** the client stream/observer side-channel (not needed silo-to-silo).

```csharp
namespace Quark.Runtime.Clustering;

public sealed class SiloPeerConnection : IAsyncDisposable
{
    public SiloPeerConnection(ITransport transport, MessageSerializer serializer,
        SiloAddress peer, ILogger<SiloPeerConnection>? logger = null);

    /// <summary>Idempotent lazy connect; safe under concurrent first calls (single-flight).</summary>
    public Task EnsureConnectedAsync(CancellationToken ct = default);

    public Task<MessageEnvelope> SendAndAwaitAsync(MessageEnvelope envelope, CancellationToken ct = default);
    public Task SendOneWayAsync(MessageEnvelope envelope, CancellationToken ct = default);

    public ValueTask DisposeAsync();
}
```

**Recommended refactor (non-blocking):** extract the shared multiplex core (`_pending` map,
`SendAndAwaitAsync`, read loop, fault-all) into an internal `MultiplexedConnection` in `Quark.Runtime`,
and have `Quark.Client.Tcp/TcpGatewayConnection` adopt it later (the client already references
`Quark.Runtime`). For *this* issue, ship `SiloPeerConnection` as a focused mirror to avoid coupling the
client refactor to silo transport; note the dedup opportunity for a follow-up.

### 4.3 `PeerConnectionManager` — lifecycle owner (new, `Quark.Runtime`, `IHostedService`)

Owns the connection pool and drives `ISiloRouter` peer registration from membership.

```csharp
namespace Quark.Runtime.Clustering;

/// <summary>
///     Watches cluster membership and installs/removes a SiloCallInvoker per Active peer into the
///     ISiloRouter, owning one pooled SiloPeerConnection per peer. Connection lifecycle: dial-on-demand,
///     pool, close-on-death. Reconnect POLICY (backoff, retry replay) is deferred to #60 — this exposes
///     the seam, it does not implement the policy.
/// </summary>
public sealed class PeerConnectionManager : IHostedService, IAsyncDisposable
{
    // On peer → Active:   router.Register(peer, new SiloCallInvoker(peer, pool.GetOrDial(peer), serializer))
    // On peer → Dead:     router.Unregister(peer); pool.Close(peer)  (fault pending; #60 adds backoff)
    // Never registers self — SiloHostedService already registers the local invoker.
}
```

Membership feed: reuse `MembershipOracle`'s existing peer-active / peer-dead transitions. The oracle
already calls `_router.Unregister(...)` on eviction (`MembershipOracle.EvictDeadSilosAsync`); this spec
adds the symmetric peer-**Register** on activation and moves the register/unregister of *remote* invokers
into `PeerConnectionManager` so the oracle stays liveness-only. (Coordinate the exact split with the
oracle owner; the oracle continues to own Dead-marking, the manager owns router mutation for peers.)

### 4.4 Active-silo snapshot for placement (new seam, `Quark.Core.Abstractions.Clustering`)

The activation miss path needs the live set of Active silos to pass to `PlacementDirector`. Reading
`IMembershipTable.ReadAllAsync()` on every miss is too costly and async-in-a-hot-path. Introduce a cached
snapshot refreshed by the oracle:

```csharp
namespace Quark.Core.Abstractions.Clustering;

/// <summary>Cheap, cached view of the currently Active silos for placement decisions.</summary>
public interface IClusterMembershipSnapshot
{
    /// <summary>Active silo addresses (includes self). Refreshed by the membership oracle.</summary>
    IReadOnlyList<SiloAddress> ActiveSilos { get; }
}
```

Default single-silo implementation returns `[self]` (so behaviour is unchanged without clustering).

### 4.5 Registration entry point (new, `Quark.Core`)

```csharp
// ISiloBuilder extension — opt-in, AOT-safe explicit wiring.
public static ISiloBuilder AddSiloToSiloTransport(this ISiloBuilder builder);
// Registers: NetworkedSiloRouter (as ISiloRouter), PeerConnectionManager (IHostedService),
//            IClusterMembershipSnapshot, and enables placement-on-activation in LocalGrainCallInvoker.
```

Clustering providers (`UseRedisClustering`, `UseKubernetes*` from #114) call this internally so a
real cross-process cluster gets silo-to-silo transport automatically; `UseLocalhostClustering` keeps the
shared-in-process `InProcessSiloRouter` (no dialing needed) and does **not** call it.

### 4.6 Compatibility tier

| Surface | Tier | Justification |
|---|---|---|
| `SiloCallInvoker`, `SiloPeerConnection`, `PeerConnectionManager`, `NetworkedSiloRouter` | **Quark-native** | Orleans' silo-to-silo messaging center is internal with no public equivalent; these are new runtime types. |
| Placement wired into activation | **drop-in (behavioural)** | Grains become placeable on remote silos exactly as Orleans places them; the `[Placement]` attributes and `PlacementDirector` API are unchanged. No user-visible API change — only that placement now actually runs. |
| `IClusterMembershipSnapshot` | **Quark-native** | New seam; Orleans exposes this via `IManagementGrain`/`MembershipTableManager` differently. |
| `AddSiloToSiloTransport()` | **Quark-native** | New explicit-wiring call; no Orleans analogue (Orleans wires the messaging center implicitly). |
| `MessageEnvelope` (`x-quark-hop` header) | **additive** | Header dictionary already exists; no struct/enum change. |

---

## 5. Placement wired into activation (closing G2 — the real "falls back local")

The change is localized to `LocalGrainCallInvoker`. Today `InvokeAsync`/`InvokeVoidAsync` call
`TryRouteRemote(grainId)` and, on `null`, go straight to `GetOrActivateAsync` (local). New logic:

1. `TryRouteRemote(grainId)` — **unchanged** fast path: directory hit for a remote owner routes now.
2. **New:** if `TryRouteRemote` returned `null` *and the request is not already a forwarded hop* (§7)
   *and* placement is enabled *and* `!_directory.TryLookup(grainId, ...)` (genuine miss), then:
   ```
   var target = _placementDirector.SelectActivationSilo(
       grainId, behaviorClass, _siloAddress, _membershipSnapshot.ActiveSilos);
   if (target != _siloAddress && _siloRouter.TryGetInvoker(target, out var remote))
   {
       _directory.TryRegister(grainId, target, out _);   // cache locally so subsequent calls fast-path
       return await remote.Invoke...(grainId, invokable, ct);
   }
   // else fall through to local activation (unchanged)
   ```
3. Otherwise activate locally, `_directory.TryRegister(grainId, _siloAddress)` (unchanged).

`behaviorClass` for placement is resolved via the existing `_typeRegistry.TryGetGrainClass(grainId.Type)`
(already used in `CreateActivationAsync`). To avoid resolving the type twice, factor a small
`ResolveBehaviorType(grainId)` helper.

**Why this is correct for hash-based placement across processes:** `SelectHashBased` orders
`availableSilos` by `(Host, Port, Generation)` and indexes by a stable FNV hash of `type|key`
(`PlacementDirector.cs:82-96`). Given the same Active-silo set, every silo computes the **same** owner.
So silo A routes G to B, and when B receives the forwarded call it computes owner=B and activates locally
— one activation, deterministic, no shared directory needed. The per-process `InMemoryGrainDirectory`
entries (A caches G→B; B holds G→B) are merely a routing cache; correctness comes from placement
determinism.

**Why non-deterministic placement is a documented limitation:** `SelectRandom`/`SelectPreferLocal`
depend on per-silo state (local silo identity, RNG). Two silos with a directory miss for the same
random-placed grain can pick different owners → two activations. The forwarded-hop guard (§7) prevents an
*infinite loop* but not the *duplicate*. Single-activation here needs a shared directory (non-goal §2,
follow-up §12-Q1). Until then, `AddSiloToSiloTransport` should log a one-time warning if a
non-deterministic strategy is used with >1 silo, and docs must state that cross-silo single-activation is
guaranteed only for `[HashBasedPlacement]`.

`availableSilos` filtering: `IClusterMembershipSnapshot.ActiveSilos` must exclude `Dead`/`ShuttingDown`
silos so placement never targets a draining peer. During a rollout, a briefly-stale snapshot may target a
just-dead peer; the router's `TryGetInvoker` miss (peer already `Unregister`ed by `PeerConnectionManager`)
falls back to local activation — safe, and the next snapshot refresh corrects it.

---

## 6. Wire protocol reuse & routing-loop prevention

**Reused unmodified:** `MessageEnvelope`, `MessageType.Request`/`OneWayRequest`/`Response`,
`GrainInvocationRequest`/`GrainInvocationResponse`, `GrainMessageSerializer`, `MessageDispatcher`,
`TransportGrainDispatcherRegistry`, `SiloMessagePump`. A silo-forwarded call is byte-identical on the
wire to a client→gateway call except for one header. The receiving `SiloMessagePump` →
`MessageDispatcher` path already deserializes the request and dispatches to the local invoker — **no
receive-side code is added**, only a branch on the new header.

**The one required extension — a hop marker (loop guard).** `MessageDispatcher.DispatchRequestAsync`
invokes the injected `_invoker` (`MessageDispatcher.cs:70,78`), which on a silo is the
`LocalGrainCallInvoker` — the very thing that runs `TryRouteRemote` + placement. Without a guard, a
forwarded call arriving at B could (under directory disagreement or non-deterministic placement) be
routed onward → potential ping-pong. Prevent it:

- `SiloCallInvoker` stamps `MessageEnvelope.Headers["x-quark-hop"] = "1"` on every outbound request.
- `MessageDispatcher` reads the header. When present, it dispatches through a **local-terminal invoker**
  that bypasses remote routing and placement (activate-here-or-fail). The cleanest AOT-safe mechanism:
  register a second `LocalGrainCallInvoker` instance constructed with `siloRouter: null` — because
  `TryRouteRemote` returns `null` immediately when `_siloRouter is null` (`:233-235`), that instance is
  inherently local-terminal with **zero new branching logic**. `MessageDispatcher` selects the terminal
  invoker for `x-quark-hop`-marked envelopes and the routing invoker otherwise.
  - Alternative (if a second DI registration is undesirable): thread an internal `bool forwarded` into
    `LocalGrainCallInvoker` and skip §5 step 2 when set. The dual-instance approach is preferred: no
    interface change, no new parameter on the hot path, reuses existing null-router semantics.
- **Hop limit = 1.** A forwarded request is never re-forwarded. Under deterministic placement this never
  even engages (owner computed == self); under non-deterministic placement it caps the pathology at one
  redundant activation instead of a loop.

**Client-originated calls are *not* marked**, so a gateway silo still routes them onward — which turns
any silo into a transparent forwarding gateway for the connected client (a free consequence: a client
on silo A can now reach a grain owned by B). Confirm the client path (`TcpGatewayCallInvoker`) does **not**
set `x-quark-hop`.

**`SiloAddress` on the wire:** grain calls do not carry a `SiloAddress` in the payload — routing is by
`GrainId` + the receiving silo's local decision — so no new codec is needed (`SiloAddress` has no field
codec today; §0 of the management-grain spec noted this). The hop marker is a string header, trivially
encoded by the existing `MessageHeaders` string map.

---

## 7. TLS / auth (coordinate with #56, do not duplicate)

Silo-to-silo links are just another `ITransport` connection. They inherit the **same**
`TcpTransportOptions.Tls` and, once #56 lands, the **same** `IConnectionAuthenticator` SPI — invoked on
both ends of the peer link:

- **Dial side** (`SiloPeerConnection.EnsureConnectedAsync` → `ITransport.ConnectAsync`): already flows
  through `TcpTransport.ConnectAsync`, which presents `Tls.LocalCertificate` as a client cert and runs
  `BuildRemoteCallback` (`TcpTransport.cs:58-77`). #56's fail-secure `AllowAny` and hostname/SAN
  validation apply here unchanged.
- **Accept side** (`SiloMessagePump` listener): #56's authenticator rejects an unauthenticated peer
  before `MessageDispatcher.DispatchAsync` — the same interception point #56 defines for the gateway.

**Coordination points to hand to #56 (not to solve here):**
1. Silo-to-silo is **mutual** auth (both ends are silos). #56's SPI must support the dialing side
   presenting an identity (silo cert), not only the accepting side. `SslClientAuthenticationOptions`
   already carries `ClientCertificates`, so no new mechanism — just confirm the SPI's principal model
   covers a "silo" principal distinct from a "client" principal.
2. Authorization granularity: a silo-forwarded call should carry the **original** caller principal, not
   be re-attributed to the forwarding silo. This spec keeps the payload identical; #56 decides whether/how
   principal identity propagates through `ICallContext` across the hop. Flag it; do not build it.

This spec adds **no** auth code. It only guarantees the peer link uses the same transport/TLS options as
the listener (a wiring assertion in `AddSiloToSiloTransport`).

---

## 8. Connection lifecycle — in scope vs. deferred to #60

| Concern | Owner | Behaviour |
|---|---|---|
| Lazy dial on first call to a peer | **#126** | `SiloPeerConnection.EnsureConnectedAsync` single-flights the connect. |
| One pooled multiplexed connection per peer | **#126** | `PeerConnectionManager` holds `SiloAddress → SiloPeerConnection`. |
| Teardown on membership Dead | **#126** | `router.Unregister(peer)` + `connection.DisposeAsync()`; pending calls faulted (`FaultAllPending`). |
| Next call after a drop may re-dial once | **#126** | A disposed/faulted connection is replaced on the next `TryGetInvoker`→invoke cycle. |
| **Bounded exponential backoff reconnect** | **#60** | Named seam: `PeerConnectionManager` reconnect hook. Not implemented here. |
| **Retry-safe replay of in-flight calls** on drop | **#60** | This spec faults pending calls (fail-fast); #60 adds idempotent replay (coordinate with dedup). |
| **Proactive directory pruning** on silo death | **#60** | This spec stops *routing* to a dead peer; pruning *directory* entries cluster-wide is #60. |
| Configurable membership timeouts | **#60 / #114** | Already covered by those specs; unchanged here. |

The failure mode this spec guarantees: a dead peer stops receiving calls promptly (oracle Dead →
`Unregister`), and a call that raced the death fails fast with a clear exception rather than hanging.
Graceful recovery (reconnect + replay) is #60's exit criterion, building on these seams.

---

## 9. AOT / trim safety

- **No reflection, no dynamic codegen.** `SiloCallInvoker`/`SiloPeerConnection` reuse the exact
  codec/serializer path (`GrainMessageSerializer`, `CodecWriter`/`CodecReader`, `MessageSerializer`)
  already proven AOT-clean by `TcpGatewayCallInvoker`/`TcpGatewayConnection`.
- **Explicit registration only** — `AddSiloToSiloTransport()` / provider `Use*Clustering()`. No assembly
  scanning, no discovery. Mirrors the repo's provider-opt-in convention.
- **No new serializable types on the wire** — grain calls are unchanged; the hop marker is a constant
  string header. No `ISerializable` (would trip QRK0003).
- **`ITransport` abstraction, not concrete `TcpTransport`** — `Quark.Runtime` keeps its existing
  dependency surface (it already resolves `ITransport` in `SiloMessagePump`); no new package edge, and no
  `Quark.Client.Tcp` reference from the runtime.
- Both new code paths sit in `Quark.Runtime` (`IsTrimmable=true`, `EnableAotAnalyzer=true` via
  `Directory.Build.props`). No `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` expected; any that
  appear signal design drift.
- **AOT smoke:** extend the existing `dotnet publish … /p:PublishAot=true` runtime smoke to a two-silo
  host referencing `AddSiloToSiloTransport` — must stay warning-free.

---

## 10. Testing strategy

Real cross-silo routing is hard to exercise in the current `TestCluster`, which shares **one** DI
container / router / directory across silos (`SharedTestClusterState`). The practical story is
**multi-silo over real TCP within one process, each silo in its own container**:

- **New `NetworkedTestCluster` harness** (or `TestClusterOptions.UseRealTransport = true`): each
  `TestSilo` gets its **own** root `IServiceProvider`, its own `InMemoryGrainDirectory`, its own
  `SiloMessagePump` bound to a distinct loopback port, a `NetworkedSiloRouter`, and a
  `PeerConnectionManager` seeded with the other silos' addresses (via a fake `IClusterMembershipSnapshot`
  or a shared in-memory membership table). This exercises the genuine dial → serialize → socket →
  dispatch → response path without leaving the test process.
- **Integration — hash-placed grain crosses a silo:** two networked silos; a `[HashBasedPlacement]` grain
  whose key hashes to silo B; call it from a client connected to silo A; assert it activates on **B**
  (assert via activation counts / a silo-id probe) and the result round-trips. This is the headline
  acceptance test.
- **Unit — `SiloCallInvoker` round-trip:** against a stub `SiloMessagePump`/dispatcher; asserts request
  envelope shape, `x-quark-hop` stamped, response deserialized, error surfaced.
- **Unit — loop guard:** a request arriving with `x-quark-hop=1` is dispatched to the local-terminal
  invoker and never re-routed even when the directory names a different owner (inject a directory that
  lies); assert no second outbound send.
- **Unit — non-deterministic placement warning:** `AddSiloToSiloTransport` + `[RandomPlacement]` + >1 silo
  logs the documented one-time warning.
- **Fault (`Quark.Tests.Fault`)** — peer connection dropped mid-call faults the pending call with a clear
  exception (fail-fast), pool re-dials on the next call; peer marked Dead → `router.Unregister` and no
  further routing to it. (Backoff/replay assertions belong to #60's suite.)
- **AOT smoke** per §9.
- **Flaky-test caution (memory):** oracle-timing tests are already flaky under parallel load; keep new
  membership/reconnect timing assertions tolerant with generous margins and prefer deterministic
  membership snapshots in unit tests.

---

## 11. Implementation sequence (circular-dep-safe, top-to-bottom)

1. `Quark.Core.Abstractions/Clustering/IClusterMembershipSnapshot.cs` — snapshot seam + default
   single-silo (`[self]`) impl location decided (abstraction = interface; default impl in `Quark.Runtime`).
2. `Quark.Transport.Abstractions` — **no change**; confirm `MessageHeaders` string map suffices for
   `x-quark-hop` (it does).
3. `Quark.Runtime/Clustering/SiloPeerConnection.cs` — pooled multiplexed peer link over `ITransport`.
4. `Quark.Runtime/Clustering/SiloCallInvoker.cs` — cross-silo `IGrainCallInvoker`; stamps `x-quark-hop`.
5. `Quark.Runtime/Clustering/NetworkedSiloRouter.cs` — map impl of `ISiloRouter` (may subclass/reuse the
   `InProcessSiloRouter` map; the distinction is which invokers get installed).
6. `Quark.Runtime/Clustering/PeerConnectionManager.cs` — `IHostedService`; membership-driven
   Register/Unregister of peer `SiloCallInvoker`s; owns the pool. Coordinate the oracle split.
7. `Quark.Runtime/DefaultClusterMembershipSnapshot.cs` — cache refreshed by `MembershipOracle`.
8. `Quark.Runtime/MessageDispatcher.cs` — read `x-quark-hop`; select the **local-terminal** invoker
   (second `LocalGrainCallInvoker` with `siloRouter: null`) for forwarded requests.
9. `Quark.Runtime/LocalGrainCallInvoker.cs` — wire `PlacementDirector` + `IClusterMembershipSnapshot`
   into the miss path (§5); inject the two new deps (optional, so single-silo/localhost is unchanged).
10. `Quark.Runtime/RuntimeServiceCollectionExtensions.cs` — register the local-terminal invoker; keep
    `PlacementDirector` (already `TryAddSingleton`).
11. `Quark.Core/Hosting/…` — `AddSiloToSiloTransport(this ISiloBuilder)`; assert TLS/auth options parity.
12. `Quark.Clustering.Redis` / `Quark.Clustering.Kubernetes` (from #114) call `AddSiloToSiloTransport`
    internally; `UseLocalhostClustering` does **not**.
13. Tests per §10; new `NetworkedTestCluster` harness in `Quark.Testing`.
14. Wiki: extend `Clustering-and-Transport.md` (silo-to-silo section, placement-determinism caveat);
    update `FEATURES.md`; note the distributed-directory follow-up.

No step references a package it must not (runtime never references client; the invoker/connection use
`ITransport` abstractions only).

---

## 12. Open questions

1. **Distributed grain directory (biggest risk).** Single-activation across processes under
   `[RandomPlacement]`/`[PreferLocalPlacement]` needs a shared directory; per-process
   `InMemoryGrainDirectory` guarantees single-activation only for `[HashBasedPlacement]`. Ship
   hash-only-correct now and file a follow-up (*distributed `IGrainDirectory`*), or expand scope to
   include a Redis-backed directory here? **Recommend:** ship hash-correct + documented limit; file the
   follow-up. Folding a distributed directory in balloons this issue and duplicates #114's Redis wiring
   decisions. **This is the load-bearing scoping call for the reviewer.**
2. **Oracle vs. manager split.** Should `PeerConnectionManager` subscribe to `MembershipOracle` events,
   or should the oracle keep calling `router.Unregister` and the manager only add peer-Register? Cleanest
   is to move all *peer* router mutation into the manager and leave the oracle liveness-only — confirm the
   oracle owner agrees.
3. **Local-terminal invoker mechanism.** Second `LocalGrainCallInvoker(siloRouter: null)` instance vs. an
   internal `forwarded` flag threaded through. Recommend the dual-instance approach (no interface/hot-path
   change); confirm no DI ambiguity from two `LocalGrainCallInvoker` registrations (use a keyed/typed
   wrapper for the terminal one).
4. **`IClusterMembershipSnapshot` refresh authority.** Oracle-pushed (recommended, one refresh per
   heartbeat) vs. pull-through-cache on `IMembershipTable`. Recommend oracle-pushed to keep the hot
   activation path allocation-free.
5. **Hop marker encoding.** `x-quark-hop` string header (simple, reuses `MessageHeaders`) vs. a new
   `MessageType.SiloRequest` enum value. Recommend the header — additive, no enum/serializer churn, and
   keeps `MessageType` semantics about response-shape not routing-origin.
6. **Should `UseLocalhostClustering` ever use the networked router?** No — in-process shared map is
   strictly cheaper and already correct. Keep the fork: shared `InProcessSiloRouter` for localhost/test,
   `NetworkedSiloRouter` + `PeerConnectionManager` for real clustering. Confirm no test depends on the
   networked path under localhost.

---

## 13. Dependencies & related work

- **#114 (k8s/DNS clustering)** — its §11 names this issue as its hard prerequisite; its Redis/DNS
  membership is what populates the Active-silo set this spec's placement path consumes. `UseRedisClustering`
  is the natural caller of `AddSiloToSiloTransport`.
- **#39 (IManagementGrain)** — its §4 documents local-only fan-out "until a TCP `ISiloRouter` exists."
  This spec provides exactly that: once `ISiloRouter.TryGetInvoker` returns real peer invokers, the
  management grain's per-silo fan-out (`localOnly` peer calls) aggregates cross-machine unchanged.
- **#56 (transport auth/TLS)** — owns the authenticator SPI and TLS hardening; this spec runs the peer
  link through it symmetrically (§7). Hard coordination, no duplication.
- **#60 (failover hygiene)** — owns reconnect backoff, retry-safe replay, and proactive directory pruning;
  this spec exposes the seams (§8) and defers the policy.
- **Follow-up (recommended, new issue):** distributed grain directory for single-activation under
  non-deterministic placement (§12-Q1).
