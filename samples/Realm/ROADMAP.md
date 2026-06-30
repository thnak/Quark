# Realm ROADMAP

Living checklist mirroring the phased plan. Check items off as merged. Do not start a phase before the previous one builds and its tests pass.

## Phase 0 — Scaffold & content

- [x] Create `samples/Realm/` projects + `Realm.slnx`; add all to `Quark.slnx`
- [x] `Realm.Common`: content models (`MapContent`, `TileGrid`, `SpawnPoint`, `MapDescriptor`), DTOs, stream-namespace constants
- [x] `Realm.Content`: 4-map (2×2) static JSON world with blocked tiles + spawns + neighbor links
- [x] Content loader service (JSON → `MapContent`), registered in DI
- [x] `README.md` + `ROADMAP.md` skeletons

## Phase 1 — Map authority (the core)

- [x] `IMapGrain` + `MapBehavior` with `IActivationMemory<MapRuntime>`, `[HashBasedPlacement]`
- [x] Tile-grid load on activation; `TryMoveAsync` collision/occupancy/bounds authority
- [x] `EnterAsync` / `LeaveAsync` roster management
- [x] Map tick via `RegisterGrainTimer` (no NPCs yet — flush empty)
- [x] Tests: collision, occupancy, bounds, enter/leave

## Phase 2 — Players & persistence

- [x] `IWorldGrain` + `WorldBehavior`: map registry, `LoginAsync` → start spawn
- [x] `IPlayerGrain` + `PlayerBehavior`: `LoginAsync` / `MoveAsync` / `LogoutAsync`
- [x] `IPersistentActivationMemory<PlayerState>` save/load; `AddInMemoryGrainStorage()`
- [x] Tests: login→spawn, persistence round-trip across deactivation

## Phase 3 — AoI broadcast & scene transitions

- [ ] Per-map stream + `DeltaBatch` codec; batched flush in the map tick
- [ ] `PlayerGrain` subscribes 3×3 map grid; `SnapshotAsync` on first subscribe
- [ ] Border-cross → leave/enter + swap subscriptions
- [ ] Tests: AoI correctness, subscription swap on transition

## Phase 4 — NPC simulation

- [ ] NPC spawn from content at activation; in-grain wander AI in the tick
- [ ] NPC deltas join the broadcast batch
- [ ] Tests: spawn count, wander stays in-bounds & respects collision

## Phase 5 — Client, viewer & perf harness

- [ ] TCP gateway client; bot driver (N players × M maps × R Hz)
- [ ] ASCII/JSON viewer rendering one map's entities live
- [ ] Metrics: latency p50/p99, msgs/sec, tick-hold, per-silo CPU/mem
- [ ] Multi-silo run (1→2→3) scaling numbers captured in `README.md`

## Phase 6 — Polish & docs

- [ ] `IQuarkDiagnosticListener` + `AddQuarkStuckGrainDetector()` wired
- [ ] AOT smoke publish of `Realm.Server`
- [ ] `README.md` finalised (run instructions, architecture diagram, results)
- [ ] Cross-link from root docs (`wiki/Samples`, `FEATURES.md` if relevant)
