# Realm — Intersect-inspired MMO spatial backbone on Quark

Realm demonstrates that Quark's POCO-actor model can back the hard core of an MMO: maps, tile grids, scene transitions, movement authority, and Area-of-Interest (AoI) broadcast. It doubles as a throughput/latency harness.

**Thesis:** in a traditional MMO server (Intersect included) each map's update loop runs on a thread pool and must take locks around shared map state. In Quark a map *is* a grain, so its mailbox serialises every call and the authoritative grid mutates lock-free. `[HashBasedPlacement]` makes each map sticky to one silo while spreading the whole world across the cluster.

## Architecture at a glance

| Grain | Cardinality | Placement | Responsibility |
|---|---|---|---|
| `WorldGrain` | 1 (singleton) | `[HashBasedPlacement]` fixed key | World directory: map registry, spawn lookup, player login → assigns starting map. |
| `MapGrain` | 1 per map | `[HashBasedPlacement]` | Authoritative tile grid, live entity roster (players + NPCs), per-tick simulation, the map's AoI broadcast stream. Sticky to one silo. |
| `PlayerGrain` | 1 per player | `[PreferLocalPlacement]` | Per-player session: current map + coords, persistent state, move-intent ingress, AoI stream subscriptions (current + 8 neighbors). |

**NPCs** are in-grain structs inside `MapGrain`, not separate grains — Intersect-faithful and far cheaper.

**AoI model:** a player subscribes to its current map's `DeltaBatch` stream plus the 8 neighbor maps' streams (3×3 map grid), so border-adjacent entities are visible.

## Project layout

```
Realm.Common/          — DTOs ([GenerateSerializer] + [Id]), content models, stream-namespace constants
Realm.GrainInterfaces/ — IWorldGrain, IMapGrain, IPlayerGrain
Realm.Grains/          — WorldBehavior, MapBehavior, PlayerBehavior (populated Phase 1+)
Realm.Content/         — static 2×2 JSON world + content loader registered via DI
Realm.Server/          — silo host: TCP gateway, in-memory streams, in-memory storage
Realm.Client/          — load-gen bots + thin viewer (the perf harness, populated Phase 5)
```

## How to run (placeholder — behaviors added in Phase 1+)

```bash
# Terminal 1 — start the silo
dotnet run --project samples/Realm/Realm.Server

# Terminal 2 — start the client
dotnet run --project samples/Realm/Realm.Client
```

The server listens on TCP gateway port **30010**. Swap `AddInMemoryGrainStorage()` → `AddRedisGrainStorage(...)` in `Realm.Server/Program.cs` for durable player-state persistence.

## Roadmap

See [ROADMAP.md](ROADMAP.md).
