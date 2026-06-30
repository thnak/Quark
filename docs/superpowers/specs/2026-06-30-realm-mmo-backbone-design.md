# Design: "Realm" — Intersect-inspired MMO spatial backbone on Quark

**Date:** 2026-06-30
**Status:** Approved (design); pending spec review → implementation plan
**Source ported:** [Intersect Engine](https://github.com/AscensionGameDev/Intersect-Engine) — an open-source C#/.NET 2D MMORPG with a traditional threaded game-loop server.
**Port fidelity:** *Concepts, fresh client.* We port Intersect's **server authority model + update-loop semantics** onto Quark POCO grains. We do **not** re-implement Intersect's wire protocol, its MonoGame client, or its EF Core schema. The client is a fresh headless load-gen + thin viewer.

## Goal

Demonstrate that Quark's POCO-actor model can back the hard core of an MMO — **maps, tile grids, scene transitions, movement authority, and Area-of-Interest (AoI) broadcast** — while doubling as a performance challenge that produces hard throughput/latency numbers.

The thesis: in a traditional MMO server (Intersect included) each map's update loop runs on a thread pool and must take locks around shared map state (entity rosters, tile occupancy). In Quark **a map *is* a grain**, so its `Channel<Func<Task>>` mailbox serialises every call and the authoritative grid mutates **lock-free**. `[HashBasedPlacement]` makes each map sticky to one silo while spreading the whole world across the cluster — horizontal scale by adding silos. The map/grid subsystem is the actor model's home turf; covering it cleanly is the bulk of "success."

## Scope — v1 = spatial backbone only

**In:** maps & tile-grid collision, server-authoritative movement, scene/border transitions, 3×3 map-grid AoI broadcast, basic NPC wander AI ticked by the map, player state persistence, a load-gen client + thin viewer that is also the perf harness.

**Out (explicit non-goals; clean v2 extensions):** combat / Intersect ABS, inventory, items/drops, crafting, trading, bank, shops, quests, dialogue; the real Intersect wire protocol; the MonoGame client; EF Core / relational DB; authentication / anti-cheat beyond server authority.

## Project layout (Quark house convention + content/harness)

```
samples/Realm/
  Realm.Common/            — static content models, message/delta DTOs, stream namespace constants, codecs
  Realm.GrainInterfaces/   — IWorldGrain, IMapGrain, IPlayerGrain
  Realm.Grains/            — WorldBehavior, MapBehavior, PlayerBehavior
  Realm.Server/            — silo host: TCP gateway, memory streams, in-memory storage
  Realm.Client/            — load-gen bots + ASCII/JSON viewer (the perf harness)
  Realm.Content/           — static map JSON (tile grids, spawn points, neighbor links)
  README.md                — what it is, how to run, the architecture-at-a-glance
  ROADMAP.md               — living checklist mirroring the roadmap below
  Realm.slnx               — opens the sample standalone
```
All `.csproj` added to `Quark.slnx`.

## Grain topology

| Grain | Cardinality | Placement | Responsibility |
|---|---|---|---|
| `WorldGrain` | 1 (singleton) | `[HashBasedPlacement]` fixed key | World directory: map registry, spawn lookup, player login → assigns starting map. No hot-path traffic. |
| `MapGrain` | 1 per map | `[HashBasedPlacement]` | **Core.** Authoritative tile grid, live entity roster (players + NPCs), per-tick simulation, the map's broadcast stream. Sticky to one silo. |
| `PlayerGrain` | 1 per player | `[PreferLocalPlacement]` | Per-player session: current map + coords, persistent state, move-intent ingress, AoI stream subscriptions (current + 8 neighbors). |

### Signed-off design decisions
- **(a) NPCs are in-grain structs, not grains.** They live in `MapGrain`'s roster and are ticked by its timer — Intersect-faithful and far cheaper than thousands of NPC grains. Players *are* grains (sessions, persistence, network identity).
- **(b) 3×3 map-grid AoI.** A player subscribes to its current map's stream + the 8 neighbor maps' streams, so border-adjacent entities are visible (Intersect's seamless map grid).
- **(c) Batched per-tick broadcast.** Movement is validated event-driven, but entity deltas are *broadcast* batched once per map tick (GPSTracker's batching trick) to bound the message rate.

## Interfaces (sketch — finalised in the plan)

```csharp
public interface IWorldGrain : IGrainWithStringKey
{
    Task<PlayerSpawn> LoginAsync(string playerId);     // → starting map id + coords
    Task<MapDescriptor> GetMapAsync(string mapId);     // neighbors, dimensions
}

public interface IMapGrain : IGrainWithStringKey
{
    Task<EnterResult> EnterAsync(string entityId, Coord at, EntityKind kind);
    Task LeaveAsync(string entityId);
    Task<MoveResult> TryMoveAsync(string entityId, Direction dir);   // tile authority
    Task<MapSnapshot> SnapshotAsync();                  // for a freshly-subscribed viewer
}

public interface IPlayerGrain : IGrainWithStringKey
{
    Task LoginAsync();                                  // world login → enter start map → subscribe AoI
    Task MoveAsync(Direction dir);                      // client ingress
    Task LogoutAsync();                                 // persist + leave map + unsubscribe
}
```

## Data flow

**Movement & AoI:**
```
client → PlayerGrain.MoveAsync(dir)
  → MapGrain.TryMoveAsync(entityId, dir)        (tile blocked? occupied?  — lock-free)
      valid → update roster, enqueue EntityDelta into pending buffer
  ── map tick (RegisterGrainTimer) ──
  → MapGrain flushes batched deltas → mapStream.OnNextAsync(DeltaBatch)
  → Quark stream → TCP gateway push → every player subscribed to this map
```

**Scene transition:** a move crossing a border → `MapGrain.LeaveAsync` returns target map + entry coords → `PlayerGrain` calls `EnterAsync` on the new map, then swaps AoI subscriptions (drop old neighbors, add new). New map may live on another silo; Quark routes transparently.

## State & persistence

- `MapBehavior` keeps `MapRuntime` (tile grid, entity roster, pending-delta buffer) in `IActivationMemory<MapRuntime>` (shell-owned, survives across calls, lost on deactivation). Rebuilt from static content + fresh NPC spawns on activation — **no persistence** (matches Intersect resetting NPCs on restart).
- `PlayerBehavior` keeps `PlayerState` (last map, coords, stub stats) in `IPersistentActivationMemory<PlayerState>`, written on logout + periodically. Server: `AddInMemoryGrainStorage()`, swappable to Redis with one line.

## Serialization & transport

- `[GenerateSerializer]` + stable `[Id]` on every cross-boundary type: `MoveIntent`, `EntityDelta`, `DeltaBatch`, `MapSnapshot`, `EntitySnapshot`, `PlayerState`, `PlayerSpawn`, `MapDescriptor`. `AddStreamableCodec<DeltaBatch, …>()` for the stream item.
- TCP gateway (`UseLocalhostGateway`); client uses `AddTcpClientStreams` for AoI push and grain proxies for ingress. AOT-clean throughout (source-gen serializers + behaviors; no reflection).

## Performance challenge (integrated, not bolted on)

`Realm.Client` is the harness: spawn **N bot players across M maps**, each issuing moves at **R Hz**. Reported metrics:
- move → broadcast latency (p50/p99),
- sustained messages/sec,
- tick-rate hold (does any map fall behind its timer?),
- per-silo CPU/memory, scaling 1→2→3 silos.

Observability via `IQuarkDiagnosticListener` + `AddQuarkStuckGrainDetector()` — a map that cannot hold its tick surfaces as a stuck-mailbox event.

## Testing

`TestCluster` in-process:
- tile collision & move validation (blocked tiles, occupied tiles, out-of-bounds),
- border handoff between two maps,
- AoI correctness (a player receives deltas only for visible maps; gains/loses neighbor streams on transition),
- NPC spawn count from content + wander tick stays in-bounds,
- player-state persistence round-trip across deactivation.

The load harness serves as the perf/soak test.

---

## Roadmap (living checklist — also mirrored in `samples/Realm/ROADMAP.md`)

> Each phase is independently buildable and demoable. Check items off as merged. Do not start a phase before the previous one builds & its tests pass.

### Phase 0 — Scaffold & content
- [ ] Create `samples/Realm/` projects + `Realm.slnx`; add all to `Quark.slnx`
- [ ] `Realm.Common`: content models (`MapContent`, `TileGrid`, `SpawnPoint`, `MapDescriptor`), DTOs, stream-namespace constants
- [ ] `Realm.Content`: 4-map (2×2) static JSON world with blocked tiles + spawns + neighbor links
- [ ] Content loader service (JSON → `MapContent`), registered in DI
- [ ] `README.md` + `ROADMAP.md` skeletons

### Phase 1 — Map authority (the core)
- [ ] `IMapGrain` + `MapBehavior` with `IActivationMemory<MapRuntime>`, `[HashBasedPlacement]`
- [ ] Tile-grid load on activation; `TryMoveAsync` collision/occupancy/bounds authority
- [ ] `EnterAsync` / `LeaveAsync` roster management
- [ ] Map tick via `RegisterGrainTimer` (no NPCs yet — flush empty)
- [ ] Tests: collision, occupancy, bounds, enter/leave

### Phase 2 — Players & persistence
- [ ] `IWorldGrain` + `WorldBehavior`: map registry, `LoginAsync` → start spawn
- [ ] `IPlayerGrain` + `PlayerBehavior`: `LoginAsync` / `MoveAsync` / `LogoutAsync`
- [ ] `IPersistentActivationMemory<PlayerState>` save/load; `AddInMemoryGrainStorage()`
- [ ] Tests: login→spawn, persistence round-trip across deactivation

### Phase 3 — AoI broadcast & scene transitions
- [ ] Per-map stream + `DeltaBatch` codec; batched flush in the map tick
- [ ] `PlayerGrain` subscribes 3×3 map grid; `SnapshotAsync` on first subscribe
- [ ] Border-cross → leave/enter + swap subscriptions
- [ ] Tests: AoI correctness, subscription swap on transition

### Phase 4 — NPC simulation
- [ ] NPC spawn from content at activation; in-grain wander AI in the tick
- [ ] NPC deltas join the broadcast batch
- [ ] Tests: spawn count, wander stays in-bounds & respects collision

### Phase 5 — Client, viewer & perf harness
- [ ] TCP gateway client; bot driver (N players × M maps × R Hz)
- [ ] ASCII/JSON viewer rendering one map's entities live
- [ ] Metrics: latency p50/p99, msgs/sec, tick-hold, per-silo CPU/mem
- [ ] Multi-silo run (1→2→3) scaling numbers captured in `README.md`

### Phase 6 — Polish & docs
- [ ] `IQuarkDiagnosticListener` + `AddQuarkStuckGrainDetector()` wired
- [ ] AOT smoke publish of `Realm.Server`
- [ ] `README.md` finalised (run instructions, architecture diagram, results)
- [ ] Cross-link from root docs (`wiki/Samples`, `FEATURES.md` if relevant)

## Open questions (resolve before/within the plan)
- Map tick rate (NPC AI Hz vs. broadcast Hz — may differ; broadcast likely 10–20 Hz, AI lower).
- World size for the default content (2×2 is enough to prove transitions; perf runs may want a larger generated grid).
- Whether `WorldGrain` is on the hot path at all (login only → no).
