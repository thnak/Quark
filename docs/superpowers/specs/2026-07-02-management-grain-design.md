# Design: IManagementGrain — cluster introspection and control
**Issue:** #39
**Date:** 2026-07-02
**Status:** Draft — ready for implementation

---

## 0. Draft-validation summary (drift found and corrected)

The issue's draft was validated line-by-line against live source. Findings:

| Draft claim | Reality | Verdict |
|---|---|---|
| `GrainActivationTable.Count` | `public int Count => _activations.Count;` (`GrainActivationTable.cs:30`) — includes *pending/faulted* entries | OK, but use `GetActiveActivations().Count` for user-facing totals |
| `GrainActivationTable.GetActiveActivations()` | exists (`:99`), returns `IReadOnlyList<(GrainId GrainId, GrainActivation Activation)>`, excludes pending/deactivating | OK |
| `GrainActivationTable.TryDeactivateAsync(grainId)` | exists (`:149`) but carries a `// TODO did not called anywhere` — currently dead code | OK — this feature becomes its first caller |
| per-type activation counting helper on the table | **does not exist** | must add, or compute in the behavior from `GetActiveActivations()` (chosen: behavior-side, no new table API) |
| `IMembershipTable.ReadAllAsync` / `MembershipEntry` | `ReadAllAsync(ct)` → `IReadOnlyList<MembershipEntry>` (`IMembershipTable.cs:12`). Member names are **`SiloAddress`, `SiloName`, `Status`, `IAmAlive`** (`MembershipEntry.cs`) — **not** `Address`/`Name` as the DTO draft implied | OK — DTO maps from these real names |
| `ISiloRouter.TryGetInvoker` | exists exactly (`ISiloRouter.cs:22`) | OK |
| fan-out "the Orleans pattern" across the cluster | **only `InProcessSiloRouter` implements `ISiloRouter`** (`InProcessSiloRouter.cs`). There is **no TCP silo-to-silo invoker** — the only remote invoker is the *client→gateway* `TcpGatewayCallInvoker`. Cross-silo fan-out therefore works **in-process only** (localhost/test clustering); genuine cross-machine fan-out is not yet possible for *any* grain | **corrected** — see §4 |
| "behavior + generated proxy ship with the runtime … no extra registration" | **False on two counts.** (1) `Quark.Runtime.csproj` does **not** reference `Quark.CodeGenerator` as an analyzer (only samples + `Quark.Tests.CodeGenerator` do) — so no proxy is generated in the runtime; it must be **hand-written**, exactly like tests. (2) Client reachability needs `AddGrainProxy<TInterface,TProxy>` in `GrainProxyFactoryRegistry` + `GrainInterfaceTypeRegistry` (`LocalGrainFactory.GetGrain` → `_interfaceRegistry.GetGrainType` → `_proxyRegistry.CreateProxy`). `AddQuarkRuntime` is server-side only and cannot register a client proxy | **corrected** — see §3 |
| DTOs "use `[GenerateSerializer]` to cross the TCP boundary" | The transport arg/result path is `GrainMessageSerializer`'s boxed `ValueKind` switch (`GrainMessageSerializer.cs:102-199`). It handles **only** null/bool/int/uint/long/ulong/string/Guid/`byte[]`/double/float/decimal/`TickStatus`/`DateTimeOffset`/`StreamId`. It does **not** handle **any collection**, `SiloAddress`, `GrainId`, `GrainType`, `DateTime`, or a top-level enum. There are **no collection codecs** in the whole codebase (`byte[]` is the only sequence). The draft DTOs (`IReadOnlyList<…>` returns, `SiloAddress`/`DateTime`/`SiloStatus` members, `GrainId`/`GrainType` args) are **not TCP-serializable today** | **corrected** — see §5, §6 |

---

## 1. Goals / Non-goals

### Goals
- Programmatic, client-reachable cluster introspection: enumerate silos, total/per-type activation counts.
- Programmatic control: force idle-collection on demand; deactivate a specific grain.
- Reachable via `IClusterClient.GetGrain<IManagementGrain>(0)` (singleton, integer key `0`).
- Works fully **in-process** at ship time (`LocalClusterClient`, `TestCluster`, multi-silo via `InProcessSiloRouter`) with **zero serialization**.
- AOT/trim-clean: hand-written proxy + typed invokable structs, no reflection, explicit registration.
- Forward-compatible with #100 (silo metadata surfaced through `GetHosts`) and #61 (graceful drain building on the control primitives).

### Non-goals
- Cross-**machine** fan-out. No TCP silo-to-silo router exists yet; this spec targets in-process fan-out and the client→gateway path only. Cross-machine aggregation is deferred to whenever a TCP `ISiloRouter` lands.
- `GetDetailedGrainStatistics` / per-activation dumps (Orleans has them) — deferred; `GetActivationStats` (per-type counts) covers the dashboard use case.
- Reminder/stream introspection — out of scope.
- Authorization / access control on management operations — deferred (note in §9).

---

## 2. Proposed API

### 2.1 Interface (`Quark.Core.Abstractions`, new namespace `Quark.Core.Abstractions.Management`)

```csharp
[GrainType("quark.management")]
public interface IManagementGrain : IGrainWithIntegerKey   // singleton, key 0
{
    ValueTask<HostsSnapshot>          GetHosts(bool onlyActive = false);
    ValueTask<int>                    GetTotalActivationCount();
    ValueTask<ActivationStatsSnapshot> GetActivationStats(string? siloAddress = null);
    ValueTask<int>                    GetGrainActivationCount(string grainType);
    ValueTask                         ForceActivationCollection(TimeSpan idleThreshold);
    ValueTask                         DeactivateGrain(GrainId grainId);
}
```

Return types are `ValueTask`/`ValueTask<T>` to match `IGrainCallInvoker` (which is `ValueTask`-returning); the hand-written proxy forwards without an `.AsTask()` hop.

### 2.2 DTOs — envelope records with serializable members only

Collections cannot be a **top-level** return value (no collection codec; and #108 explicitly excludes top-level args). They *can* be members of a `[GenerateSerializer]` type once #108 lands. So every list return is wrapped in an envelope record whose single member is an `ImmutableArray<T>`. `SiloAddress` is flattened to its `ToString()`/`Parse` string form; `DateTime` becomes `DateTimeOffset`; enums are fine **as members** (the generator maps them to their underlying integer).

```csharp
[GenerateSerializer]
public sealed record HostsSnapshot(
    [property: Id(0)] ImmutableArray<SiloStatusEntry> Hosts);

[GenerateSerializer]
public sealed record SiloStatusEntry(
    [property: Id(0)] string       Address,     // SiloAddress.ToString(); SiloAddress.Parse to rehydrate
    [property: Id(1)] string       Name,        // MembershipEntry.SiloName
    [property: Id(2)] SiloStatus   Status,      // enum member — serializer-supported
    [property: Id(3)] DateTimeOffset IAmAlive); // was DateTime in draft — DateTime is NOT transport-serializable

[GenerateSerializer]
public sealed record ActivationStatsSnapshot(
    [property: Id(0)] ImmutableArray<GrainActivationStat> Stats);

[GenerateSerializer]
public sealed record GrainActivationStat(
    [property: Id(0)] string Type,   // GrainType.Value — GrainType has no codec
    [property: Id(1)] int    Count);
```

Method args are likewise restricted to transport-serializable shapes: `string siloAddress` / `string grainType` (not `SiloAddress`/`GrainType`, which have no codecs). `DeactivateGrain(GrainId)` keeps `GrainId` because `GrainId` is the invoke *routing key* and is already carried by the envelope; however as a **method argument** it still routes through `GrainMessageSerializer` and would fail — so the invokable serializes it as its two string components (`Type.Value`, `Key`). See §5.

### 2.3 Compatibility tier — **minor-change** (per member)

Orleans ships `IManagementGrain`, so the *concept* is drop-in, but every signature differs (Orleans uses `SiloAddress[]`, `Dictionary<SiloAddress,int>`, `DetailedGrainStatistic[]`). Quark's shapes are envelope-wrapped immutable arrays with string-flattened addresses to fit the current serializer. Net: **minor-change** — same grain, same intent, Quark-native DTO shapes. `GetHosts` / `GetTotalActivationCount` names are drop-in; `GetActivationStats` / `ForceActivationCollection` / `DeactivateGrain` are Quark-native equivalents of `GetDetailedGrainStatistics` / `ForceActivationCollection` / `DeactivateAsync`.

---

## 3. Runtime integration & auto-registration (real anchors)

### 3.1 Where the types live (package boundaries)

- **Interface + DTOs** → `Quark.Core.Abstractions` (`Management/` folder). Both silo and client reference it. (DTOs are value/record types — consistent with the "abstractions = interfaces + value types" rule.)
- **Hand-written proxy + invokable structs + transport dispatcher** → **`Quark.Core`** (referenced by both `Quark.Runtime` and `Quark.Client`; `Quark.Core` already references `Quark.Serialization` for `CodecWriter`/`CodecReader`). A proxy is an implementation, so it must **not** go in `*.Abstractions`. This is the shared home the generator would occupy if it ran — since it does not, we hand-author the same artifacts here.
- **`ManagementGrainBehavior`** (the POCO `IGrainBehavior`) → `Quark.Runtime` (needs `GrainActivationTable`, `IMembershipTable`, `ISiloRouter`, idle-collection).

The **`[GenerateSerializer]` DTOs still need generated codecs.** Since no `src/` project runs `Quark.CodeGenerator`, the DTO codecs must be **hand-written** `IFieldCodec<T>`/`IDeepCopier<T>` alongside the DTOs in `Quark.Core`, OR `Quark.Serialization.Abstractions` gains the generator as an analyzer for this one file set. Recommendation: **hand-write the DTO codecs** in `Quark.Core` (matches the "tests hand-write" convention; avoids adding an analyzer to a shipping package). They are only exercised on the TCP path (§6, gated on #108).

### 3.2 Why the code generator is not used (decision)

Adding `Quark.CodeGenerator` as an analyzer to `Quark.Runtime` was considered and **rejected**:
1. It would emit the proxy into `Quark.Runtime`, but the **client** must never depend on runtime internals for the proxy, and the client needs the proxy at DI time.
2. `BehaviorRegistrationGenerator` would emit an assembly-wide `AddQuarkRuntimeBehaviors()` for every `IGrainBehavior` in the runtime — surface creep into a foundational package.
3. Hand-writing matches the established test convention and keeps the runtime analyzer-free.

### 3.3 Server auto-registration — extend `AddQuarkRuntime` (`RuntimeServiceCollectionExtensions.cs`)

Append to `AddQuarkRuntime`, after the activation-table / dispatcher registrations:

```csharp
// Built-in management grain (issue #39)
services.AddTransient<ManagementGrainBehavior>();
services.AddSingleton<IGrainBehaviorRegistration>(
    new GrainBehaviorRegistration(new GrainType("quark.management"), typeof(ManagementGrainBehavior)));
services.AddGrainTransportDispatcher(
    new GrainType("quark.management"), new ManagementGrainProxy_TransportDispatcher());
```

`ManagementGrainBehavior` is annotated `[GrainBehavior("quark.management")]` so its type key resolves to `quark.management` (matching the `[GrainType]` on the interface). Registration is idempotent-safe via the existing deferred `IGrainBehaviorRegistration` marker applied in `BehaviorStartupValidator`/registry `Apply`.

### 3.4 Client registration — **not** automatic; new opt-in call

`AddQuarkRuntime` is silo-side only and cannot populate `GrainProxyFactoryRegistry`/`GrainInterfaceTypeRegistry`. Add:

```csharp
// Quark.Client / ClientServiceCollectionExtensions.cs
public static IServiceCollection AddQuarkManagementClient(this IServiceCollection services)
    => services.AddGrainProxy<IManagementGrain, ManagementGrainProxy>("quark.management");
```

Called explicitly by the consumer, and wired into `AddLocalClusterClient()` and the TCP client builder so that in-process clients and `TestCluster` get it for free. The explicit `"quark.management"` grain-type-name argument is **required** because `AddGrainProxy`'s default derives the name from the proxy class name (`ManagementGrainProxy` → `ManagementGrain`), which would not match the server key.

### 3.5 Behavior implementation anchors

`ManagementGrainBehavior` constructor injects (all already DI-registered singletons):
- `GrainActivationTable` — `GetActiveActivations()` for counts and per-type grouping (`GrainId.Type`), `TryDeactivateAsync(grainId)` for `DeactivateGrain`.
- `IMembershipTable` — `ReadAllAsync()` → map `MembershipEntry` → `SiloStatusEntry`.
- `ILocalSiloDetails` — to identify the local silo and skip self during fan-out.
- `ISiloRouter` (optional) — peer invokers for fan-out (§4).
- an **on-demand idle-collection runner** for `ForceActivationCollection` (§3.6).

`GetTotalActivationCount` / `GetGrainActivationCount` / `GetActivationStats` compute from `GetActiveActivations()` grouped by `GrainId.Type` — **no new `GrainActivationTable` API required** (drops the draft's "per-type counting helper" checklist item).

### 3.6 On-demand collection — factor out of `GrainIdleCollector`

`GrainIdleCollector` (`GrainIdleCollector.cs`) is an `internal sealed BackgroundService` whose sweep is `internal void CollectIdleGrains()` (`:39`), hard-coded to `_options.GrainCollectionAge` and iterating `_activationTable.GetActiveActivations()` + `activation.IsIdleLongerThan(age, now)`. For `ForceActivationCollection(TimeSpan idleThreshold)`:

- Extract the sweep into a new singleton `IdleCollectionRunner` (in `Quark.Runtime`) exposing `void Collect(TimeSpan age)`.
- `GrainIdleCollector` calls `runner.Collect(_options.GrainCollectionAge)`; `ManagementGrainBehavior.ForceActivationCollection` calls `runner.Collect(idleThreshold)`.
- Register `IdleCollectionRunner` as a singleton in `AddQuarkRuntime`; `GrainIdleCollector` takes it via ctor.

(Note: the real option name is `SiloRuntimeOptions.GrainCollectionAge`, not `CollectionAge` as the top-level docs abbreviate.)

---

## 4. Multi-silo fan-out

Fan-out is **in-process only** today (only `InProcessSiloRouter` exists; §0). Design:

- `GetHosts` reads `IMembershipTable.ReadAllAsync()` — already cluster-wide, **no fan-out needed**.
- `GetTotalActivationCount` / `GetActivationStats` / `GetGrainActivationCount` are **per-silo local** facts. To aggregate the cluster, the local behavior enumerates membership, and for each **peer** silo (skipping self via `ILocalSiloDetails`) resolves a peer invoker via `ISiloRouter.TryGetInvoker(peerAddress, out invoker)`, then invokes the peer's `quark.management` activation with a **local-scope flag** to prevent recursive fan-out (peer returns only its own local counts). Merge the immutable arrays.
- Because `TryGetInvoker` only ever returns in-process peers today, aggregation is correct within one process (localhost/test clustering) and silently returns local-only results in a genuine multi-machine deployment (documented limitation until a TCP `ISiloRouter` exists).

To avoid infinite recursion, the fan-out methods take an internal `bool localOnly` — the public interface methods call the behavior with `localOnly:false`; peer calls pass `localOnly:true`. Model this as **two internal invokable variants** (or a hidden overload on the behavior), not extra interface members, so the public surface stays clean.

---

## 5. Transport serialization detail (the hard part)

The transport arg/result path is `GrainMessageSerializer` (boxed `ValueKind` switch) for bare args, and generated/hand-written codecs for `[GenerateSerializer]` members. Constraints that shaped the DTOs:

| Type | Transport-serializable? | Resolution |
|---|---|---|
| `IReadOnlyList<T>` / `List<T>` / `T[]` (any collection, top-level or member) | **No** (no collection codec anywhere) | wrap in `[GenerateSerializer]` envelope with `ImmutableArray<T>` member → **depends on #108** |
| `ImmutableArray<T>` **as a member of a `[GenerateSerializer]` type** | Not yet — **#108 delivers exactly this** | dependency (§8) |
| `SiloAddress` | No (custom struct, no codec, not in `ValueKind`) | flatten to `string` via `ToString()`/`Parse` |
| `GrainType` | No | flatten to `string` (`.Value`) |
| `GrainId` (as method **arg**) | No (routing key ≠ arg codec) | invokable serializes `Type.Value` + `Key` as two strings |
| `DateTime` | **No** (only `DateTimeOffset` is in the switch) | use `DateTimeOffset` |
| `SiloStatus` enum **as member** | Yes (generator maps enum→underlying int) | keep as member |
| `int`, `string`, `bool`, `TimeSpan`(arg) | `TimeSpan` **not** in `ValueKind` switch | `ForceActivationCollection(TimeSpan)` invokable serializes it as `long Ticks` |

Net: DTOs use only `string`/`int`/`DateTimeOffset`/enum members inside `[GenerateSerializer]` envelopes with `ImmutableArray<T>` collection members. This makes the **only** external serialization dependency **#108** (immutable-collection codecs); everything else (string flattening, `Ticks`, two-string `GrainId`) is under this feature's control in the hand-written invokables.

---

## 6. Two-phase delivery

**Phase A — in-process (ship now, no serialization dependency).**
Invokable structs are **local-only**: `Serialize` is a no-op and `DeserializeResult` throws `NotSupportedException` (the established pattern, e.g. `WorkerGrain_DoWorkInvokable.cs`). Fully functional under `LocalClusterClient`, `TestCluster`, and in-process multi-silo fan-out. Rich DTO member types would be fine here since nothing serializes — but we still author the transport-safe shapes from §2 so Phase B needs no DTO churn.

**Phase B — TCP gateway client reachability (gated on #108).**
Fill in real `Serialize`/`DeserializeResult` in the invokables and the hand-written DTO codecs. Requires #108's `ImmutableArray<T>`-member codec support. Until #108 lands, calling the management grain from a `TcpGatewayClusterClient` throws `NotSupportedException` at the (de)serialize boundary — acceptable and clearly documented.

---

## 7. AOT notes

- Hand-written proxy + typed invokable structs → no reflection, no dynamic dispatch, all types visible to the linker.
- No new `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` surfaces.
- DTO codecs hand-written (or #108-generated) — no `ISerializable` (would trip QRK0003).
- Explicit DI registration on both silo (`AddQuarkRuntime`) and client (`AddQuarkManagementClient`); no assembly scanning.
- Enum-as-member and string flattening keep every serialized field within the existing trim-clean codec set.

---

## 8. Dependencies & related work

- **#108 — immutable-collection codecs** (`docs/superpowers/specs/2026-07-02-immutable-collection-codecs-design.md`, *Planned*). **Hard dependency for Phase B only.** Its scope covers `ImmutableArray<T>` as a member of a `[GenerateSerializer]` type — exactly the envelope pattern here. Its non-goals explicitly exclude top-level immutable-collection args, which is why we wrap in envelope records. If #108's mutable-collection follow-up (`List<T>`/`IReadOnlyList<T>`) lands instead, the DTOs could return `IReadOnlyList<T>` directly, but the envelope+`ImmutableArray` form is the safer target.
- **#100 — silo metadata** (parallel spec `2026-07-02-silo-metadata-design.md`, not yet on disk). `SiloStatusEntry` is the surfacing point: once #100 defines per-silo metadata, add an `[Id(4)] ImmutableDictionary<string,string> Metadata` (or flattened) member to `SiloStatusEntry` and populate it in `GetHosts`. Reserve `Id(4)` now; coordinate the DTO shape with the #100 author.
- **#61 — graceful drain** (planned). `DeactivateGrain` + `ForceActivationCollection` + the extracted `IdleCollectionRunner` are the control primitives graceful drain will orchestrate (drain = set `SiloStatus.ShuttingDown`, then force-collect + deactivate remaining). No API overlap; #61 builds on these.

---

## 9. Open questions

1. **Authorization.** Management ops (deactivate, force-collect) are unauthenticated once reachable. Ship with a note, or gate behind an opt-in `EnableManagementGrain` flag / a marker on the client? Recommend: ship reachable in-process, add an opt-in flag before exposing over the TCP gateway.
2. **Client auto-wiring.** Fold `AddQuarkManagementClient()` into `AddLocalClusterClient()` unconditionally, or keep it opt-in so trimming can drop the management proxy when unused? Recommend: fold into `AddLocalClusterClient` (in-process, tiny) but keep it a separate call on the TCP client builder.
3. **`GetTotalActivationCount` semantics.** `GetActiveActivations().Count` (excludes pending/faulted) vs `Count` (raw). Recommend `GetActiveActivations().Count` to match Orleans "activations", but confirm dashboards don't expect pending included.
4. **`localOnly` recursion guard shape.** Two internal invokable variants vs. a hidden behavior overload vs. an ambient scope flag. Recommend internal invokable variants (cleanest, no `ICallContext` extension — `ICallContext` exposes only `GrainId` and must not grow, per #78).

---

## 10. Implementation sequence (circular-dep-safe, top-to-bottom)

1. `Quark.Core.Abstractions/Management/IManagementGrain.cs` — interface (`[GrainType("quark.management")]`).
2. `Quark.Core.Abstractions/Management/*.cs` — `HostsSnapshot`, `SiloStatusEntry`, `ActivationStatsSnapshot`, `GrainActivationStat` (`[GenerateSerializer]` records).
3. `Quark.Core/Management/ManagementDtoCodecs.cs` — hand-written `IFieldCodec`/`IDeepCopier` for the four DTOs (Phase B; Phase A can stub with `NotSupportedException` bodies matching the local-only invokable convention).
4. `Quark.Core/Management/ManagementInvokables.cs` — one `IGrainInvokable<T>`/`IGrainVoidInvokable` struct per method (+ internal `localOnly` variants). Phase A: `Serialize` no-op, `DeserializeResult` throws.
5. `Quark.Core/Management/ManagementGrainProxy.cs` — hand-written proxy (`IManagementGrain`, `IGrainProxyActivator<ManagementGrainProxy>`) routing through `IGrainCallInvoker`; plus `ManagementGrainProxy_TransportDispatcher`.
6. `Quark.Runtime/IdleCollectionRunner.cs` — extracted sweep (`Collect(TimeSpan age)`); refactor `GrainIdleCollector` to consume it.
7. `Quark.Runtime/Management/ManagementGrainBehavior.cs` — `[GrainBehavior("quark.management")]` POCO; local reads + in-process fan-out.
8. `Quark.Runtime/RuntimeServiceCollectionExtensions.cs` — register behavior + transport dispatcher + `IdleCollectionRunner` in `AddQuarkRuntime`.
9. `Quark.Client/ClientServiceCollectionExtensions.cs` — `AddQuarkManagementClient()`; call it from `AddLocalClusterClient`.
10. `Quark.Client.Tcp/*` — call `AddQuarkManagementClient()` from the TCP client builder (Phase B reachability).
11. Tests (§11).

---

## 11. Test plan

- **Unit (`Quark.Tests.Unit`)**: `ManagementGrainBehavior` against a real `GrainActivationTable` + fake `IMembershipTable` — `GetHosts` mapping, `GetTotalActivationCount`, `GetActivationStats` grouping, `GetGrainActivationCount`, `DeactivateGrain` removes the activation (`TryGetActivation` → false), `ForceActivationCollection` deactivates grains idle past a custom threshold (drive via `IdleCollectionRunner`).
- **Integration (`Quark.Tests.Integration`, `TestCluster`)**: single silo — `GetGrain<IManagementGrain>(0)` returns hosts/counts; activate N grains, assert counts; `DeactivateGrain` then re-query. Hand-write the proxy/invokables (no generator in test projects) — reuse the shipped `Quark.Core` proxy directly.
- **Multi-silo (`TestCluster` with 2 in-process silos sharing `InProcessSiloRouter`)**: activate grains on both, assert `GetActivationStats`/`GetTotalActivationCount` aggregate across silos and the `localOnly` guard prevents double-counting/recursion.
- **Fault (`Quark.Tests.Fault`)**: `DeactivateGrain` on an unknown `GrainId` is a no-op (`TryDeactivateAsync` already handles this); membership read failure surfaces cleanly.
- **AOT smoke**: confirm `dotnet publish … /p:PublishAot=true` on `Quark.Runtime` stays warning-free with the new behavior/registration.
- **Phase B (deferred until #108)**: TCP gateway round-trip of `HostsSnapshot`/`ActivationStatsSnapshot` — asserts `ImmutableArray` member codecs and string-flattened address roundtrip.
