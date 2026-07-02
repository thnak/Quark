# Design: Silo metadata (tags/labels)
**Issue:** #100
**Date:** 2026-07-02
**Status:** Draft — ready for implementation

Orleans reference: dotnet/orleans#8189. Lets a silo declare arbitrary
`key=value` metadata (e.g. `region=us-east`, `tier=gpu`) that is stored in the
membership record and surfaced for diagnostics — and, later, for affinity-based
placement.

## 1. Goals / Non-goals

### Goals
- Let an operator declare static, per-silo metadata (`region`, `tier`, `zone`, …)
  at host-configuration time on `ISiloBuilder`.
- Carry that metadata on the silo's `MembershipEntry` so it lives with the silo's
  cluster record and is visible to any component that already reads membership
  (diagnostics, a future `IManagementGrain.GetHosts`).
- Ship the MVP the issue explicitly asks for: **declaration + storage + exposure.**

### Non-goals (explicitly deferred)
- **Placement integration.** No `[MetadataPlacement]` / affinity director in this
  work item. Sketched only (§7).
- **Auto-detection magic.** No implicit import of environment variables, cloud
  instance metadata (IMDS), or Kubernetes downward-API labels by default. An
  env-var import helper is offered as an *opt-in, explicit* call (§3), never a
  default behavior.
- **Mutable / runtime-updatable metadata.** Metadata is set once at silo
  configuration and is immutable for the silo's lifetime. Runtime mutation +
  re-advertisement is a separate feature.
- **Wire format for remote membership.** Membership is in-process today (§5); a
  distributed membership table that puts `MembershipEntry` on the wire is out of
  scope here and owned by the k8s-clustering spec.

## 2. Proposed API

**Compatibility tier: Quark-native.** Orleans #8189 is itself an open proposal
with no shipped stable surface, so there is no drop-in target. The names chosen
mirror Orleans' *conceptual* vocabulary (`SiloMetadata`) to ease a later
alignment.

### 2a. Declaration on options (`Quark.Runtime`)

Extend `SiloRuntimeOptions`:

```csharp
public sealed class SiloRuntimeOptions
{
    // ...existing members...

    /// <summary>
    ///     Static operator-declared metadata (tags/labels) for this silo, e.g.
    ///     <c>region=us-east</c>, <c>tier=gpu</c>. Carried on this silo's
    ///     <see cref="MembershipEntry"/> and surfaced via diagnostics and the
    ///     management grain. Immutable for the silo's lifetime. Empty by default.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
}
```

### 2b. Builder ergonomics (`Quark.Runtime`, `RuntimeSiloBuilderExtensions`)

```csharp
/// <summary>Adds or overwrites a single silo metadata tag.</summary>
public static ISiloBuilder AddSiloMetadata(this ISiloBuilder builder, string key, string value);

/// <summary>Adds or overwrites a batch of silo metadata tags.</summary>
public static ISiloBuilder AddSiloMetadata(
    this ISiloBuilder builder, IEnumerable<KeyValuePair<string, string>> metadata);

/// <summary>
///     Opt-in import of environment variables matching <paramref name="prefix"/>
///     (default <c>QUARK_SILO_META_</c>) as metadata tags, with the prefix stripped
///     and the remainder lower-cased. Explicit — never invoked automatically.
/// </summary>
public static ISiloBuilder AddSiloMetadataFromEnvironment(
    this ISiloBuilder builder, string prefix = "QUARK_SILO_META_");
```

Implementation note (for the dev agent, not part of the contract): these compose
over the existing `builder.Configure<SiloRuntimeOptions>(...)` path used by
`UseLocalhostClustering`. Because `Metadata` is `IReadOnlyDictionary`, the
`AddSiloMetadata` helpers build a fresh mutable copy, apply the mutation, and
reassign — keeping the exposed property immutable to consumers.

### 2c. Storage on the membership record (`Quark.Core.Abstractions`)

Extend `MembershipEntry`:

```csharp
public sealed class MembershipEntry
{
    // ...existing SiloAddress / SiloName / Status / IAmAlive...

    /// <summary>
    ///     Operator-declared metadata (tags/labels) for the silo, copied from
    ///     <c>SiloRuntimeOptions.Metadata</c> when the row is written. Empty when
    ///     the silo declared none. Read-only to consumers.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = EmptyMetadata;

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
```

`init`-only: an entry's metadata is fixed when the row is inserted; the oracle's
`UpdateIAmAliveAsync` / status flips do not touch it.

## 3. Runtime integration (anchors)

Concrete edit sites, in dependency order:

1. **`src/Quark.Core.Abstractions/Clustering/MembershipEntry.cs`** — add the
   `Metadata` property (§2c). Abstractions-only, no behavior.
2. **`src/Quark.Runtime/SiloRuntimeOptions.cs`** — add the `Metadata` property
   (§2a).
3. **`src/Quark.Runtime/Clustering/MembershipOracle.cs`** — the oracle is the
   single writer of this silo's row. In `ExecuteAsync`'s initial
   `InsertRowAsync(new MembershipEntry { ... })`, copy `_options.Metadata` onto
   the entry. `MarkSelfDeadAsync` and `UpdateIAmAliveAsync` are unchanged (they
   must not clobber metadata — `UpdateRowAsync` in `MarkSelfDeadAsync` rebuilds
   the entry, so also carry `_options.Metadata` there to preserve it).
4. **`src/Quark.Runtime/RuntimeSiloBuilderExtensions.cs`** — add the three
   `AddSiloMetadata*` helpers (§2b).
5. **Diagnostics exposure (§4).**

Read-side consumers get metadata for free: `IMembershipTable.ReadAllAsync`
already returns full `MembershipEntry` objects, so anything reading membership
(the parallel management grain, a future placement director) sees `.Metadata`
without further plumbing.

**Placement anchor (for the deferred follow-up, documented not built):**
`IPlacementDirector.SelectActivationSilo` currently receives only
`IReadOnlyList<SiloAddress> availableSilos` — addresses, no metadata. A
metadata-aware strategy would need either (a) the director to be handed
`IReadOnlyList<MembershipEntry>` instead of bare addresses, or (b) an injected
`IMembershipTable`/oracle lookup to resolve `SiloAddress → Metadata`. Note the
director is wired but has no in-runtime caller yet, so this reshaping is
low-risk when it happens. Out of scope here; see §7.

## 4. Exposure

1. **Diagnostics.** Metadata is available on `MembershipEntry` for any diagnostic
   listener that inspects membership. If a lifecycle/membership diagnostic event
   is added later, include the tag map. No new event struct is *required* for the
   MVP — the goal is "queryable," satisfied by membership read access.
2. **Management grain.** The primary structured exposure is
   `IManagementGrain.GetHosts` in the parallel spec
   `2026-07-02-management-grain-design.md`. That grain reads
   `IMembershipTable.ReadAllAsync` and can project `.Metadata` into its host
   detail DTO. **Coordinate the host-detail DTO shape there** so it includes the
   metadata map; this spec only guarantees the data is present on the entry.

## 5. Serialization considerations

**Honest current state: membership never crosses the wire.** Verified:
- `InMemoryMembershipTable` stores entries in a process-local
  `ConcurrentDictionary`; `SharedLocalhostCluster` is a process-scoped registry.
- `grep` finds **no** `[GenerateSerializer]` on `MembershipEntry` and no codec
  referencing it. It is never serialized anywhere in the tree today.

Therefore adding `IReadOnlyDictionary<string,string>` to `MembershipEntry` is
**not a wire concern for this work item.** It is a plain in-process object graph.

**Forward note (no work here):** if a *distributed* membership table is added —
e.g. the DNS/Kubernetes clustering design in
`2026-07-02-kubernetes-dns-clustering-design.md` — `MembershipEntry` (or a
provider-specific projection of it) will need a serialized form, and **there is
no `Dictionary`/`IReadOnlyDictionary` field codec in Quark.Serialization today**
(only the 18 primitive codecs + generated `[GenerateSerializer]` types). At that
point the map must be encoded explicitly. Recommended contract to hand the
distributed-membership author: serialize metadata as a length-prefixed sequence
of `(string key, string value)` pairs with `Ordinal` key ordering for
determinism — a bespoke encoding in that provider, **not** a general dictionary
codec, and **not** `ISerializable` (QRK0003). This spec does not build any of
that; it only pins the recommended shape so the two specs agree. Cross-reference:
`2026-07-02-kubernetes-dns-clustering-design.md`.

## 6. AOT notes

- Pure POCO property additions and DI-composed option mutation — no reflection,
  no dynamic code, no assembly scanning. Trim/AOT-clean by construction.
- `AddSiloMetadataFromEnvironment` reads `Environment.GetEnvironmentVariables()`,
  which is AOT-safe; no annotations needed.
- Nothing here introduces `ISerializable` or a runtime dictionary codec, so no
  new QRK0001–QRK0003 exposure.

## 7. Deferred: `[MetadataPlacement]` sketch (not designed)

A follow-up could add a `[MetadataPlacement("region", "us-east")]` (or
predicate/affinity form) behavior attribute resolved by a new
`MetadataPlacement` strategy. Its director would filter `availableSilos` to those
whose `MembershipEntry.Metadata` satisfies the requirement (hard constraint) or
rank them by match (soft affinity), falling back to random when none match. This
requires reshaping `IPlacementDirector.SelectActivationSilo` to receive silo
*entries* (with metadata) rather than bare `SiloAddress` values (§3 anchor).
Deferred to its own issue/spec; called out here only so the storage design does
not foreclose it — which it does not, since metadata is already on the entry.

## 8. Test plan

Unit (`Quark.Tests.Unit`):
- `SiloRuntimeOptions.Metadata` defaults to empty (non-null).
- `AddSiloMetadata(key,value)` and the batch overload populate options; later
  calls overwrite same-key entries.
- `AddSiloMetadataFromEnvironment` imports only prefixed vars, strips prefix,
  lower-cases the remainder, and imports nothing when no var matches.
- `MembershipEntry.Metadata` defaults to empty; round-trips a set map via `init`.

Integration (`Quark.Tests.Integration`, `TestCluster`):
- A silo configured with `AddSiloMetadata("region","us-east")` produces a
  `MembershipEntry` (read back via `IMembershipTable.ReadAllAsync`) whose
  `.Metadata["region"] == "us-east"`.
- IAmAlive heartbeat and self-dead transition do **not** drop metadata
  (assert after an oracle update cycle).
- Metadata-less silo yields an empty (non-null) map.

## 9. Implementation checklist (top-to-bottom, no circular deps)

1. `src/Quark.Core.Abstractions/Clustering/MembershipEntry.cs` — add `Metadata`
   (`init`, empty default).
2. `src/Quark.Runtime/SiloRuntimeOptions.cs` — add `Metadata` (empty default).
3. `src/Quark.Runtime/Clustering/MembershipOracle.cs` — copy `_options.Metadata`
   onto the entry in `InsertRowAsync` and in `MarkSelfDeadAsync`'s
   `UpdateRowAsync`.
4. `src/Quark.Runtime/RuntimeSiloBuilderExtensions.cs` — add `AddSiloMetadata`
   (single + batch) and `AddSiloMetadataFromEnvironment`.
5. Tests per §8.
6. Docs: note the feature in `wiki/Clustering-and-Transport.md`; update
   `FEATURES.md` parity row for #100.

## 10. Resolved design decisions

- **Declaration home:** `SiloRuntimeOptions.Metadata` + `ISiloBuilder` helpers —
  consistent with how `ClusterId`/`SiloName`/`SiloAddress` are already declared.
- **No auto-detection default:** env-var import is an explicit opt-in helper, per
  the issue's own guidance and to keep behavior predictable and trim-safe.
- **Immutable:** metadata is fixed at config time; `IReadOnlyDictionary` +
  `init`. Runtime mutation is a separate feature.
- **Storage on `MembershipEntry`:** co-locates metadata with the silo's cluster
  identity so all existing membership readers get it for free.
- **No wire work now:** membership is in-process; serialization is deferred to
  and coordinated with the distributed-membership spec.
- **Placement deferred:** MVP is declaration + storage + exposure only.

## 11. Dependencies & related work

- **`2026-07-02-management-grain-design.md`** (parallel) — primary structured
  consumer via `GetHosts`; coordinate the host-detail DTO to carry the metadata
  map.
- **`2026-07-02-kubernetes-dns-clustering-design.md`** (parallel) — owns any
  remote membership wire format; must adopt the length-prefixed `(key,value)`
  pair encoding from §5 if/when `MembershipEntry` metadata goes over the wire.
- **Diagnostics** (`Quark.Diagnostics.Abstractions`) — secondary consumer.
- **Placement** (`IPlacementDirector`) — future `[MetadataPlacement]` follow-up
  (§7), not part of this work item.
