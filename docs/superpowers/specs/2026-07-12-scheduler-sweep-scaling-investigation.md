# ActivationScheduler sweep scaling past N≈32 — investigation

Investigates GitHub issue #164, on top of commit `947f528`. No fix is proposed or implemented here —
per the issue's own scoping, this establishes whether the current full-shard-sweep design is
actually a problem at N=64/128 before anyone designs a fix.

## 1. Background

`ActivationScheduler` (`src/Quark.Runtime/ActivationScheduler.cs`) shards its ready queue into
`N = SchedulerMaxConcurrentActivations` independent `ConcurrentQueue<GrainActivation>` instances,
but every worker sweeps every shard (`TryDequeueAny`, O(N) per attempt) rather than owning one
exclusively — deliberate, since it's what preserves the "N workers configured means N activations
can truly run concurrently" guarantee regardless of hash collisions (`Spec6`/`Spec7` in
`ActivationSchedulerTests.cs`). The wake-signal-sharding follow-up
(`2026-07-09-scheduler-wake-signal-sharding-design.md`) made the *wake* O(1) but explicitly left the
sweep itself untouched. Every prior measurement in this area
(`2026-07-08-scheduler-ready-queue-contention-fix.md`, `2026-07-09-work-stealing-scheduler-design.md`,
`2026-07-09-scheduler-wake-signal-sharding-design.md`) was taken at N=32 on this same 32-core box.
Issue #164 asks: does the sweep design hold up at N=64/128, or at N configured above physical core
count, and does static hash sharding degrade under "few hot activations vs. large configured N"
skew? It lists two deferred fix candidates (ThreadPool piggyback vs. a custom higher-N-aware design)
but proposes neither — only asks for benchmarks and a `dotnet-trace` breakdown first.

## 2. A finding that reframes the whole investigation: `ActivationScheduler` is not the runtime default

Before any of the numbers below: `RuntimeServiceCollectionExtensions.AddQuarkRuntime()` registers
`SimpleActivationScheduler` — an unbounded `Task.Run`-per-activation fallback that **ignores
`SchedulerMaxConcurrentActivations` entirely** — not the sharded `ActivationScheduler` under
investigation here. This is a documented, deliberate choice (see that method's "KNOWN HAZARD"
comment): the sharded scheduler has a reproducible bounded-worker-pool reentrancy deadlock for
grain-to-grain nested calls, confirmed via the Realm sample's TCP bot-driver benchmark, and hasn't
been fixed. **The design this issue asks about is not currently shipping as the default dispatch
path in any Quark silo.**

Every benchmark below that needed `ActivationScheduler` had to explicitly override the DI
registration (`services.AddSingleton<IActivationScheduler>(sp => new ActivationScheduler(...))`
placed after `AddQuarkRuntime()`, which wins DI's last-registration-wins single-resolution semantics
over `AddQuarkRuntime()`'s `TryAddSingleton` default). This is safe specifically for `PingPong`'s and
`SchedulerSkew`'s workloads because their call pattern is entirely client-driven (an external loop
calling into grains one hop at a time) — never a nested grain-to-grain call from inside another
grain's own behavior method, which is what the known deadlock requires. It would **not** be safe to
force for `AstroSim` (each chunk grain calls up to 26 neighbor chunk grains from inside its own tick
behavior — exactly the nested shape that triggers the deadlock), so the AstroSim regression check
below deliberately runs on the unmodified default (`SimpleActivationScheduler`), matching production.

**Practical implication for prioritization:** any fix to the sweep's O(N) cost only matters once
`ActivationScheduler` is actually safe to re-enable as the default. The reentrancy deadlock is
arguably the more load-bearing blocker right now — worth flagging to whoever triages #164 next to
whichever issue tracks that deadlock (not found under a specific number in this pass).

## 3. Benchmarks added

- **`PingPongRunner --scheduler-workers N`** (`tests/Quark.Performance/PingPong/PingPongRunner.cs`):
  decouples `SchedulerMaxConcurrentActivations` from `--pairs` and core count, and forces
  `ActivationScheduler` in (see §2). Ignored under `--bare`.
- **`SchedulerSkewRunner`** (new, `tests/Quark.Performance/SchedulerSkew/`): a small, fixed number of
  continuously-volleying "hot" ping/pong pairs (`--hot-pairs`, default 2) against an independently
  configured `--scheduler-workers` N (default 128) — the "few hot activations vs. large N" shape.
  Reports ready-queue wait-time percentiles (via a diagnostics listener on
  `OnSchedulerActivationWaited`) in addition to raw calls/s, since throughput alone conflates every
  other per-call cost.

Both are documented in `tests/Quark.Performance/README.md`.

### 3.1 A second pre-existing bug found and fixed along the way

`AddQuarkRuntime()` registers `NullDiagnosticListener` via `TryAddSingleton<IQuarkDiagnosticListener>`.
`AddQuarkDiagnostics(...)`'s own registration is *also* a `TryAddSingleton` for the same service type
— so whichever of the two is called first wins, and every existing `TestCluster`-based runner calls
`AddQuarkRuntime()` first. Concretely: **`AstroSimRunner`'s "Total messages"/"msg/s" figures were
silently always reporting 0** (its `BenchmarkDiagnosticListener` never received a single event), and
`SchedulingQualityRunner`'s ready-queue-wait/drain-duration histograms have the same problem
(verified: `n=0` on every run, independent of this issue). Fixed it in `AstroSimRunner.cs` (needed a
working throughput signal for the regression check in §6) with an explicit `AddSingleton` override
that wins regardless of call order, and used the same pattern in the new `SchedulerSkewRunner`.
**`SchedulingQualityRunner`'s copy of this bug is left unfixed** — out of scope for #164, flagged here
for a follow-up.

## 4. Methodology

- All throughput numbers below are **medians of 3 interleaved 10s trials** at the N=32/128 extremes
  (single runs at N=64, since the extremes are what matters for "does it degrade" and repeated
  trials at every point would have cost more wall-clock than the signal warranted).
  Machine: 32 cores, 62GB RAM — the same box every prior scheduler spec used. Configuring N=64/128
  here is "N above core count," not real many-core hardware.
- `dotnet-trace --profile dotnet-sampled-thread-time` + `dotnet-trace convert --format speedscope` +
  a Python parent-frame/self-time analysis script, same tool family as the three prior scheduler
  specs. Their script was written to isolate `Monitor.Enter_Slowpath`'s parent frame specifically
  (the design was lock-contention-bound at the time); the current design is lock-free
  (`ConcurrentQueue`/`Interlocked`), so this round's script instead reports the top self-time frames
  overall and a parent-frame breakdown keyed on `RunWorkerAsync`/`TryDequeueAny`/`ActivationScheduler`.

## 5. Results

### 5.1 PingPong, low demand vs. N (`--pairs 4`, i.e. 8 activations against N shards)

| N | calls/s (median of 3, 10s trials) |
|---|---|
| 32 | 296,797 |
| 64 | 320,725 *(single 15s run)* |
| 128 | 300,449 |

**No degradation exceeding trial-to-trial noise.** The 3-trial spreads at N=32 (292.7K–301.5K) and
N=128 (284.2K–304.5K) overlap almost entirely. A single earlier 15s run at each N (327.6K / 320.7K /
299.0K) suggested an ~8.7% drop, but that didn't survive repeated trials — a reminder that single-run
throughput figures on this box are not reliable evidence on their own.

### 5.2 PingPong, control shape (`--pairs 32`, i.e. 64 activations — demand ≈ the historical default N)

| N | calls/s (median of 3, 10s trials) |
|---|---|
| 32 | 350,198 |
| 64 | 311,857 *(single 15s run)* |
| 128 | 292,657 |

**A real, repeatable regression.** All 3 trials at N=128 (269.6K–302.7K) fall below all 3 trials at
N=32 (334.0K–353.1K) — non-overlapping distributions, ≈17% median drop. This is the shape that most
directly matches the issue's "aggregate O(N²) idle-churn" theory: at N=32 the 32 configured workers
are close to fully occupied servicing the 32 pairs' volleys (little idle-park churn); raising N to
128 while demand stays at 32 pairs turns ~75% of workers into a mostly-idle population that pays the
sweep/park cost on every cycle. This is a *sharper* demonstration of the pathology than the
low-demand shape in §5.1, because §5.1's demand was already far below N=32 to begin with — scaling N
further doesn't change the qualitative regime the way it does here.

### 5.3 SchedulerSkew (`--hot-pairs 2`, i.e. 4 activations against N shards) — the opposite result

| N | calls/s (median of 3, 10s trials) | wait-time mean / p99 |
|---|---|---|
| 32 | 185,777 | 2.3–2.4us / 7.6–8.2us |
| 64 | 177,346 *(single run)* | 2.1us / 8.5us |
| 128 | 213,724 | 1.6–1.7us / 4.0–4.6us |

**Throughput and wait-time both *improve* from N=32 to N=128**, the opposite of the hypothesized
degradation, and by a comfortable margin (wait-time p99 roughly halves). The most plausible
explanation: with only 4 busy activations, the birthday-paradox collision probability onto the *same*
shard is much higher at N=32 (~4 keys into 32 buckets) than at N=128 (~4 keys into 128 buckets) — more
shards means the handful of busy activations are less likely to contend on the same
`ConcurrentQueue`, and at this extremely low demand level that contention-reduction benefit
apparently outweighs the extra sweep cost, at least through N=128 on this hardware. **The "few hot
activations vs. large N" framing in the issue does not hold as a monotonic degradation** — it depends
on how "few" vs. "N" compares to §5.2's demand-saturates-then-exceeds-N regime, not on skew alone.

### 5.4 `dotnet-trace` (PingPong control shape, N=32 vs. N=128, 15s capture mid-run)

| | N=32 | N=128 |
|---|---|---|
| Threads sampled | 58 | 58 |
| Total sampled thread-time | 872,106 ms | 869,944 ms |
| `UNMANAGED_CODE_TIME` | 436,597 ms (50.1%) | 382,911 ms (44.0%) |
| `CPU_TIME` | 431,375 ms (49.5%) | 485,751 ms (55.8%) |
| `Monitor.Enter_Slowpath` self-time | ~0 (negligible) | ~0 (negligible) |
| Traced run's own throughput | 331,819 calls/s | 191,980 calls/s |

Two things worth separating:

1. **`Monitor.Enter_Slowpath` stays negligible at both N** — confirms the lock-free redesign from the
   prior specs holds; this is not a lock-contention regression the way the pre-2026-07-08 design was
   (which had 67K-75K contended samples at the time).
2. **`CPU_TIME`'s share of total sampled thread-time grew from 49.5% to 55.8%** (and
   `UNMANAGED_CODE_TIME` shrank correspondingly) scaling N 32→128, while the traced run produced ~42%
   fewer calls/s — directionally consistent with more CPU cycles going toward scheduler bookkeeping
   per unit of useful work at higher N. **This is not conclusive on its own**: `dotnet-sampled-thread-time`
   collapses almost all managed-frame self-time into these two synthetic leaf buckets (every named
   frame we could find, including `RunWorkerAsync`/`TryDequeueAny`/`ScheduleAsync`, showed ~0.0ms
   self-time even though their *sample/call counts* were non-trivial — e.g. `ScheduleAsync` sampled
   2,098 times at N=128 vs 3,352 at N=32 over the same window), so this profile type cannot attribute
   the CPU_TIME growth to the sweep loop specifically versus other N-scaling costs (e.g. more Task
   continuations queued onto the same fixed-size CLR ThreadPool — note the *OS thread count* stayed
   at 58 in both traces, meaning N=128's extra "workers" are Tasks competing for the same physical
   threads, not new OS threads). Also note the traced run's throughput drop (~42%) is larger than the
   untraced 3-trial median drop in §5.2 (~17%) — `dotnet-trace`'s own sampling overhead likely scales
   with N too (more concurrently-scheduled Tasks to sample), so the *traced* throughput numbers should
   not be read as the real effect size; §5.2's untraced trials are the trustworthy throughput figure.
   **Recommendation for whoever picks this up next:** re-profile with a provider/profile that gives
   finer per-method attribution than the two-bucket collapse here (e.g. `--profile cpu-sampling` with
   an explicit provider list) if a precise sweep-cost attribution is needed before designing a fix.

## 6. AstroSim regression check (default `SimpleActivationScheduler`, unmodified)

Sanity scale (`--bodies 100000 --grid 8 --duration 5`, after the §3.1 diagnostics fix):
118,235 msg/s sustained, bodies stayed within world bounds ([44.8, 755.0] vs. [0, 800]).

Full check (`--bodies 1000000 --grid 32 --duration 15`): 6 ticks completed, 4,983,504 total messages,
323,128 msg/s sustained throughput, ramping cleanly from 243.8K msg/s (t=3s) to 323.1K msg/s (t=15s)
with no stalls. Final chunk center-of-mass bounds [31.3, 3177.1] vs. world [0, 3200] — bodies remain
gravitationally bound, not collapsing or flying apart. **No regression** on this "real per-tick work"
shape, distinct from PingPong/SchedulerSkew's idle-churn shape — expected, since this run uses the
default scheduler, untouched by anything in this investigation.

## 7. Test suite regression check

`dotnet build Quark.slnx -c Release` — clean, 0 warnings, 0 errors. `dotnet test
tests/Quark.Tests.Unit -c Release --no-build`, run 3 times: 536/537 passed every time, with exactly 1
failure per run rotating between two already-known timing-sensitive flaky tests
(`StuckGrainDetectorStallTests.ExecuteAsync_FiresOnSchedulerDrainStalled_WhenActivationLivelocked`,
`ReminderServiceTests.UnregisterReminder_StopsFutureFirings` — the latter matches a flakiness already
documented in the 2026-07-09 wake-signal-sharding plan), both confirmed passing in isolation. **Zero
`SchedulingSemantics` failures across all 3 runs.** Neither flaky test touches scheduler code; this
investigation's changes are benchmark-only (`tests/Quark.Performance/`) plus this doc.

## 8. Non-goals

- No fix implemented or proposed for the sweep's O(N) cost, matching the issue's own scoping.
- `Spec6`/`Spec7`/`ActivationSchedulerConcurrencyStressTests` were **not** re-parametrized or re-run
  at N=64/128 — those tests are deliberately hardcoded to specific low N (1, 2) to reproduce specific
  races, and "passing at multiple N values" is fix-validation work that only makes sense once a
  specific redesign exists to validate. Deferred.
- `SchedulingQualityRunner`'s copy of the §3.1 diagnostics-wiring bug is not fixed here.
- The `ActivationScheduler` reentrancy deadlock (§2) is not investigated or fixed here — flagged as
  arguably higher-priority than the sweep question, since it's why the sweep design isn't even the
  runtime default today.

## 9. Honest conclusion

The picture is genuinely mixed, not a clean "yes" or "no":

- **Confirmed**: the O(N) sweep design does cost real throughput when configured demand is at or
  below the pre-existing default N and N is then raised well past it (§5.2, ~17% at N=128 vs N=32,
  non-overlapping across 3 trials) — this is the clearest evidence in this investigation matching the
  issue's core theory, and the trace in §5.4 shows a directionally consistent CPU_TIME-share increase
  at the same N, though not root-caused to the sweep loop specifically.
- **Not confirmed / reversed**: when demand is *already* far below N before scaling further (§5.1),
  or when demand is extremely low and sparse (§5.3, `SchedulerSkew`), scaling N shows no reliable
  degradation, and in the sparsest case actually **improves** throughput and latency — likely because
  fewer shards means more hash-collision contention among the few busy activations, and that cost
  dominates over sweep cost at this end of the spectrum.
- **Structural finding that changes the priority calculus**: `ActivationScheduler` isn't the
  production default scheduler today (§2) — `SimpleActivationScheduler` is, specifically because of
  an unrelated, unresolved reentrancy deadlock. Any sweep-scaling fix only becomes load-bearing once
  that's resolved.

**Recommendation**: this doesn't yet justify picking one of the two deferred fix candidates
(ThreadPool piggyback vs. custom higher-N design) — the regression is real but specifically tied to
the "N configured above realistic peak demand" regime (§5.2), not a general "higher N is worse"
result, and the reentrancy deadlock is arguably the more urgent blocker regardless. Worth a follow-up
design spec if/when `ActivationScheduler` is made safe to re-enable as the default, scoped around the
demand-saturates-N regime specifically rather than the skew framing, and re-profiled with a
finer-grained trace provider than used here.
