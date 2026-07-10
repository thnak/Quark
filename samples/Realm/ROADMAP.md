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

- [x] Per-map stream + `DeltaBatch` codec; batched flush in the map tick
- [x] `PlayerGrain` subscribes to its current map + cardinal neighbors (the content model only
      tracks N/S/E/W links, not diagonals, so this is a "plus" shape rather than a full 3×3 grid);
      `SnapshotAsync` on first subscribe to each newly in-range map so already-present entities
      aren't missed
- [x] Border-cross → leave/enter + swap subscriptions
- [x] Tests: AoI correctness, subscription swap on transition

## Phase 4 — NPC simulation

- [x] NPC spawn from content at activation (new `npcSpawns` field per map, separate from player
      `spawnPoints`); in-grain wander AI runs each map tick — every NPC has a per-tick chance to
      take one random cardinal step, subject to the same bounds/blocked-tile/occupancy checks as
      player movement. NPCs never cross map borders (an out-of-bounds step is just skipped).
- [x] NPC deltas join the broadcast batch — wander steps are queued into the same
      `PendingDeltas`/`DeltaBatch` pipeline built in Phase 3, so subscribed players see NPC
      movement for free.
- [x] Tests: spawn count (NPC roster matches content `npcSpawns`), wander stays in-bounds &
      respects collision (sampled repeatedly across several tick periods)

## Phase 5 — Client, viewer & perf harness

- [x] TCP gateway client; bot driver (N players × M maps × R Hz) — `Realm.Client` is now a
      one-shot batch load generator (`--players`/`--rate`/`--duration`/`--gateway-port`).
      `WorldBehavior.LoginAsync` was changed from "always the first map with spawn points" to a
      deterministic per-playerId hash across all maps with spawn points, so N bots genuinely
      spread across the world's M maps instead of piling onto one.
- [ ] ASCII/JSON viewer rendering one map's entities live — deferred; out of scope for the batch
      bot-driver shape this phase shipped (see README "How to run" for the load-gen-only design).
- [x] Metrics: latency p50/p99, msgs/sec, AoI delta throughput — printed by the bot driver
      (`--- Results ---` block). `tick-hold` and `per-silo CPU/mem` are not implemented: they'd
      need `IQuarkDiagnosticListener` wiring (Phase 6) and/or OS-level process sampling that
      wasn't part of this pass.
- [ ] Multi-silo run (1→2→3) scaling numbers — **not implemented; real framework gap found.**
      `UseLocalhostClustering` alone does not spread *fresh* grain activations across multiple
      in-process silos: a client connected to one silo's gateway has every new
      `HashBasedPlacement` grain activate locally on that same silo regardless of silo count
      (confirmed empirically — 40 fresh activations across 2 in-process silos, all landed on the
      gateway silo). Cross-silo *calls to an already-active* grain do route correctly; only fresh
      placement doesn't spread. `AddSiloToSiloTransport()` doesn't fix it either without real
      peer-discovery config. Running the bot driver at silo counts 1/2/3 would show flat, not
      scaling, numbers — deferred until the framework supports real activation spread.

### Framework bug found and fixed while building this phase

Driving the bot driver at realistic concurrency (20 players, 2 moves/sec, 15s) against a real TCP
gateway reproducibly hung `Quark.Runtime`'s scheduler — the first workload in this repo to exercise
real concurrent TCP-driven grain-call load hard enough to expose it (`TestCluster`-based unit tests
never do). Initially suspected as a lost-wakeup race in the sharded `ActivationScheduler` (added
just before this phase — see `docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md`
and `2026-07-09-scheduler-wake-signal-sharding-design.md`); a follow-up dig root-caused the exact
mechanism instead: a **bounded-worker-pool reentrancy deadlock**, not a wake-signal timing bug.
`ActivationScheduler`'s dispatch loop treats a worker as fully busy for the whole time it's
synchronously awaiting a nested cross-activation call (the ordinary shape of any grain-to-grain
call). If enough concurrently in-flight calls fan into a shared, not-yet-serviced target activation
to exceed `SchedulerMaxConcurrentActivations`, every worker can end up transitively blocked waiting
on a target only a worker — and every worker is blocked — could service. Reproduced reliably with
an isolated, TCP-free unit repro at worker counts 1/2/4, plus a diagnostics-listener trace showing
a stranded activation's ready-queue entry sitting unserviced for 6+ seconds after a successful
schedule. Full writeup lives in `ActivationScheduler`'s class remarks
(`src/Quark.Runtime/ActivationScheduler.cs`). `AddQuarkRuntime()` now falls back to the older
`SimpleActivationScheduler`, which is structurally immune (unbounded `Task.Run` per activation, no
fixed worker pool to exhaust) — see the comment on the registration in
`src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs` — confirmed via the same 20-bot benchmark
(600/600 moves, 0 failed, p99 ~46-125ms) and the full `Quark.Tests.Unit` suite (497/497 passing). A
real fix requires restructuring the dispatch loop so it doesn't treat "blocked on a nested call" as
"busy" (e.g. transient extra capacity for reentrant calls); not yet implemented. Also hardened
`GrainActivationTable.GetOrCreateAsync`, which cached a `ValueTask<GrainActivation>` and handed the
same struct instance to every concurrent caller of an already-active grain — `ValueTask` is
documented as unsafe for multiple/concurrent consumers, unlike `Task`; fixed by caching a `Task`
and wrapping a fresh `ValueTask` per call. This was a real correctness hazard but, per direct A/B
testing, was not the cause of the scheduler hang.

## Phase 6 — Polish & docs

- [x] `IQuarkDiagnosticListener` + `AddQuarkStuckGrainDetector()` wired — `Realm.Server` now logs
      stuck/livelocked activations via `RealmDiagnosticsListener`
      (`samples/Realm/Realm.Server/RealmDiagnosticsListener.cs`). Wiring this up surfaced a second
      real framework bug: **found and fixed** a circular-DI deadlock in
      `Quark.Diagnostics.DiagnosticsServiceCollectionExtensions.AddQuarkDiagnostics` — its
      `CompositeDiagnosticListener` depended on `IEnumerable<IQuarkDiagnosticListener>`, which
      included the composite's own self-referencing factory registration, so resolving it recursed
      forever (reproduced as a genuine silo-startup hang, confirmed with and without the stuck
      detector). Independently hit and worked around earlier in `Quark.Performance`'s AstroSim /
      PingPong / Fairness benchmark runners (see `docs/superpowers/specs/
      2026-07-08-astro-sim-benchmark-design.md` §5) — those workarounds are now removed since the
      real fix supersedes them. See `src/Quark.Diagnostics/DiagnosticsServiceCollectionExtensions.cs`
      and regression tests in `tests/Quark.Tests.Unit/Diagnostics/DiagnosticsCompositeRegistrationTests.cs`.
- [ ] AOT smoke publish of `Realm.Server` — **blocked; real tooling gap found, not Realm-specific.**
      `dotnet publish -r linux-x64 /p:PublishAot=true` fails during *restore* (not build) with
      `NETSDK1207` against `Quark.CodeGenerator.csproj` (netstandard2.0, doesn't support AOT).
      `Realm.Grains`/`Realm.Common`/`Realm.GrainInterfaces` reference the generator as an analyzer
      via the standard `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` pattern, but
      NuGet's restore-graph walk (`_GenerateProjectRestoreGraph` → `ProcessFrameworkReferences`)
      evaluates *every* `ProjectReference` — including analyzer-only ones — with the same global
      properties (`PublishAot=true`, `RuntimeIdentifier=linux-x64`) as the root project, before any
      per-edge Build-time metadata can override them. Tried and confirmed ineffective:
      `SkipGetTargetFrameworkProperties`, `UndefineProperties`, `SetTargetFramework` — none of them
      change what the restore-graph target sees. A two-step `restore` (RID only, no `PublishAot`)
      then `publish --no-restore` "succeeds" but silently skips real AOT compilation (produces a
      254-file framework-dependent-looking output, not a single native binary) — confirmed not a
      real fix. Reproduces identically on `samples/Adventure/Adventure.Server`, so this affects
      every sample that uses `Quark.CodeGenerator`, not just Realm. A real fix needs the generator
      referenced some other way (e.g. a pre-built `<Analyzer Include="...\CodeGenerator.dll">` item
      instead of `ProjectReference`) — a repo-wide wiring change across every sample/test project
      referencing the generator (12 files), out of scope for this pass.
- [x] `README.md` finalised (run instructions, architecture diagram, results)
- [x] Cross-link from root docs (`wiki/Samples`, `FEATURES.md` if relevant — `FEATURES.md` is an
      Orleans-parity tracker with no samples section, so only `wiki/Samples.md` applies)
