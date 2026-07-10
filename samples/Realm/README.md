# Realm — Intersect-inspired MMO spatial backbone on Quark

Realm demonstrates that Quark's POCO-actor model can back the hard core of an MMO: maps, tile grids, scene transitions, movement authority, and Area-of-Interest (AoI) broadcast. It doubles as a throughput/latency harness.

**Thesis:** in a traditional MMO server (Intersect included) each map's update loop runs on a thread pool and must take locks around shared map state. In Quark a map *is* a grain, so its mailbox serialises every call and the authoritative grid mutates lock-free. `[HashBasedPlacement]` makes each map sticky to one silo while spreading the whole world across the cluster.

## Architecture at a glance

| Grain | Cardinality | Placement | Responsibility |
|---|---|---|---|
| `WorldGrain` | 1 (singleton) | `[HashBasedPlacement]` fixed key | World directory: map registry, spawn lookup, player login → assigns starting map. |
| `MapGrain` | 1 per map | `[HashBasedPlacement]` | Authoritative tile grid, live entity roster (players + NPCs), per-tick simulation, the map's AoI broadcast stream. Sticky to one silo. |
| `PlayerGrain` | 1 per player | `[PreferLocalPlacement]` | Per-player session: current map + coords, persistent state, move-intent ingress, AoI stream subscriptions (current + 8 neighbors). |

**NPCs** are in-grain structs inside `MapGrain`, not separate grains — Intersect-faithful and far cheaper. Each map spawns NPCs from its content-defined `npcSpawns` and runs a simple wander AI each tick (random cardinal step, respecting the same bounds/blocked-tile/occupancy rules as players; NPCs never cross map borders).

**AoI model:** a player subscribes to its current map's `DeltaBatch` stream plus its cardinal (N/S/E/W) neighbor maps' streams — a "plus" shape, not a full 3×3 grid, since the content model only tracks 4-directional neighbor links. `SnapshotAsync` on first subscribe to a newly in-range map catches the player up on already-present entities.

```
                         TCP gateway (port 30010)
                                  │
                        ┌─────────┴─────────┐
                        │   Realm.Client     │  bot-driver load harness
                        │  (N players × Rhz) │  or an interactive client
                        └─────────┬─────────┘
                                  │ MoveAsync / LoginAsync
                                  ▼
  ┌───────────────┐      ┌───────────────┐        AoI DeltaBatch streams
  │  WorldGrain    │◄────►│  PlayerGrain  │◄──────────────┐
  │  (singleton)   │ login │ (per player) │                │
  │  map registry  │ spawn └───────┬───────┘                │
  └───────────────┘               │ TryMoveAsync            │
                                   ▼                         │
                        ┌────────────────────┐               │
                        │      MapGrain       │──────────────┘
                        │  (1 per map, sticky  │   subscribes current map
                        │   via HashBasedPlacement) │  + N/S/E/W neighbors
                        │  tile grid + roster  │
                        │  (players + in-grain  │
                        │   NPC structs)        │
                        │  per-tick simulation  │
                        └──────────┬───────────┘
                                   │ N/S/E/W neighbor links (content model)
                                   ▼
                          adjacent MapGrain(s)
```

## Project layout

```
Realm.Common/          — DTOs ([GenerateSerializer] + [Id]), content models, stream-namespace constants
Realm.GrainInterfaces/ — IWorldGrain, IMapGrain, IPlayerGrain
Realm.Grains/          — WorldBehavior, MapBehavior, PlayerBehavior (populated Phase 1+)
Realm.Content/         — static 2×2 JSON world + content loader registered via DI
Realm.Server/          — silo host: TCP gateway, in-memory streams, in-memory storage
Realm.Client/          — batch bot-driver load harness (Phase 5); no live viewer yet
```

## How to run

```bash
# Terminal 1 — start the silo
dotnet run --project samples/Realm/Realm.Server

# Terminal 2 — run the bot-driver load harness
dotnet run --project samples/Realm/Realm.Client -- --players 20 --rate 2 --duration 15
```

The server listens on TCP gateway port **30010**. Swap `AddInMemoryGrainStorage()` → `AddRedisGrainStorage(...)` in `Realm.Server/Program.cs` for durable player-state persistence.

`Realm.Client` is a one-shot batch load generator, not an interactive client: it logs in `--players`
bots (spread across every map with spawn points, via a deterministic per-playerId hash in
`WorldBehavior.LoginAsync`), drives each one at `--rate` random-direction moves/sec for
`--duration` seconds over one shared TCP gateway connection, then prints latency/throughput and
exits. Flags: `--players` (default 20), `--rate` (moves/sec/bot, default 2.0), `--duration`
(seconds, default 15), `--gateway-port` (default 30010). There is no live ASCII/JSON viewer yet —
see [ROADMAP.md](ROADMAP.md) Phase 5 for why it was deferred.

### Sample results

20 players, 2 moves/sec/bot, 15s, single silo, both processes on the same localhost machine:

```
Bots spread across 4 map(s).
--- Results ---
Duration:         15.01s
Total moves:      600 (0 failed)
Throughput:       40.0 moves/sec
Move latency p50: 1.82 ms
Move latency p99: 124.86 ms
Move latency max: 125.25 ms
AoI deltas recv:  14864 (avg 743.2/bot)
```

Multi-silo (1→2→3) scaling numbers are not included — see the ROADMAP Phase 5 note on a real
framework gap (fresh grain activations don't spread across in-process silos yet) found while
building this harness.

## Diagnostics

`Realm.Server` wires up `AddQuarkDiagnostics<RealmDiagnosticsListener>()` and
`AddQuarkStuckGrainDetector()` (see `Realm.Server/RealmDiagnosticsListener.cs`), so a livelocked or
stuck map/player activation shows up as a warning in the silo's console log instead of silently
degrading throughput. Wiring this up for this sample surfaced and fixed a real circular-DI bug in
`Quark.Diagnostics` — see the ROADMAP Phase 6 note.

## AOT

Native AOT publish (`dotnet publish -r linux-x64 /p:PublishAot=true`) currently fails for this
sample — and every other sample that uses `Quark.CodeGenerator` — due to a NuGet restore-graph
limitation unrelated to Realm's own code. See the ROADMAP Phase 6 note for the full diagnosis.

## Roadmap

See [ROADMAP.md](ROADMAP.md).
