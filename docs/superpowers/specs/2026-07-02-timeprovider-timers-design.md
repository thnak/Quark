**Issue:** #118 — TimeProvider support for grain timers  
**Date:** 2026-07-02  
**Status:** Planned — design also posted as a comment on the issue

# Design: TimeProvider support for grain timers (#118)

## Goals / Non-goals

**Goals**
- Make `RegisterGrainTimer` firing driven by an injectable `System.TimeProvider` so timer-driven grain behavior can be tested deterministically without wall-clock delays.
- Default to `TimeProvider.System` — zero behavior change for existing hosts.
- Expose a `TimeProvider` override on `TestCluster` so a test can supply `FakeTimeProvider` and advance virtual time.
- Keep the change AOT/trim clean and additive (no breaking public API changes).

**Non-goals**
- **Idle-age / collection clocking is explicitly deferred.** `GrainIdleCollector` uses `DateTimeOffset.UtcNow` (line 41), `GrainActivation._lastAccessedTicks` uses `DateTimeOffset.UtcNow.UtcTicks` (ctor line 71), and the collection loop uses `PeriodicTimer`. Threading `TimeProvider` through idle collection is a larger, independent surface (3 clock sites + `PeriodicTimer` -> `TimeProvider.CreateTimer` loop rewrite + `IsIdleLongerThan`/`IsDeactivationAllowed`/`DelayDeactivation` all take an injected `now`). It deserves its own issue. This design touches **grain timers only**.
- No per-timer `TimeProvider` option on `GrainTimerCreationOptions` (see Resolved decisions).
- Reminders (`IReminderService`) are a separate scheduling subsystem and out of scope.

## Proposed API

No changes to any Orleans-facing public surface. All new wiring is internal except the one test-harness property.

**`Quark.Testing.Harness.TestClusterOptions`** (new public property):
```csharp
/// <summary>
///     Optional clock for grain timers on every silo in the cluster. When set, it is
///     registered as the silo-wide <see cref="TimeProvider"/> singleton so timers created
///     via RegisterGrainTimer fire on this clock. Supply a FakeTimeProvider to advance
///     time deterministically. Default: null (uses TimeProvider.System).
/// </summary>
public TimeProvider? TimeProvider { get; set; }
```

**Internal shape changes** (signatures only, no bodies):

`GrainTimer<TState>` — swap `System.Threading.Timer` for `ITimer`:
```csharp
internal GrainTimer(
    Func<TState, CancellationToken, Task> callback,
    TState state,
    GrainTimerCreationOptions options,
    Func<Func<ValueTask>, ValueTask> postToQueue,
    TimeProvider timeProvider);   // NEW trailing param
// field: private readonly ITimer _timer;  (was System.Threading.Timer)
// ctor body: _timer = timeProvider.CreateTimer(OnFire, null, options.DueTime, options.Period);
```

`GrainActivation` — carry the silo clock, default `TimeProvider.System` for back-compat:
```csharp
public GrainActivation(
    GrainId grainId,
    GrainType grainType,
    bool isReentrant,
    IServiceProvider root,
    ILogger<GrainActivation> logger,
    IQuarkDiagnosticListener? diagnostics = null,
    int mailboxCapacity = 0,
    MailboxFullMode mailboxFullMode = MailboxFullMode.Wait,
    TimeProvider? timeProvider = null);   // NEW optional trailing param; ?? TimeProvider.System
```

`LocalGrainCallInvoker` — accept the clock and pass it to each `GrainActivation`:
```csharp
public LocalGrainCallInvoker(
    ...existing params...,
    IQuarkDiagnosticListener? diagnostics = null,
    TimeProvider? timeProvider = null);   // NEW optional trailing param; ?? TimeProvider.System
```

`GrainTimerCreationOptions` — **unchanged** (stays drop-in with Orleans).

## Compatibility tier

**Minor-change / Quark-native-additive.** Orleans has no `TimeProvider` on `GrainTimerCreationOptions`, so there is nothing to be drop-in *against* here; the user-facing `RegisterGrainTimer` / `GrainTimerCreationOptions` surface is untouched and stays drop-in. The new capability is injected via DI (a silo-wide `TimeProvider` singleton), which is Quark's standard "minor-change" wiring convention. All modified constructors take the new parameter as an **optional trailing arg**, so existing call sites (including hand-written test proxies and fault-test fixtures that `new` these types directly) compile unchanged.

## Runtime integration (real anchors)

1. **`src/Quark.Runtime/GrainTimer.cs:11,25`** — change field `private readonly Timer _timer;` to `private readonly ITimer _timer;`; change ctor line 25 from `new Timer(OnFire, null, options.DueTime, options.Period)` to `timeProvider.CreateTimer(OnFire, null, options.DueTime, options.Period)`. `_timer.Change(...)` (line 31) and `_timer.Dispose()` (line 37) are already `ITimer` members — no other edits. `OnFire`'s state signature is unchanged.

2. **`src/Quark.Runtime/GrainActivation.cs:51-72`** — add `TimeProvider? timeProvider = null` param; store `private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;`. At line 211 (`RegisterTimer`), pass `_timeProvider` as the new trailing arg to `new GrainTimer<TState>(...)`. The probe ctor at line 90 does not register timers, so it needs no clock (defaults to `System` if ever touched).

3. **`src/Quark.Runtime/LocalGrainCallInvoker.cs:36-64,285`** — add optional `TimeProvider? timeProvider = null` ctor param; store `_timeProvider = timeProvider ?? TimeProvider.System`. At line 285, add `_timeProvider` as the trailing arg to `new GrainActivation(...)`.

4. **`src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs:24,69-81`** — in `AddQuarkRuntime`, add `services.TryAddSingleton<TimeProvider>(TimeProvider.System);`. In the `LocalGrainCallInvoker` factory (line 69), append `timeProvider: sp.GetService<TimeProvider>() ?? TimeProvider.System`. Using `TryAddSingleton` means any earlier registration (e.g. from TestSilo) wins.

5. **`src/Quark.Testing/Harness/TestClusterOptions.cs`** — add the `TimeProvider? TimeProvider` property above.

6. **`src/Quark.Testing/Harness/TestSilo.cs:91`** — immediately **before** `Options.ConfigureSiloServices?.Invoke(...)`, add:
   `if (Options.TimeProvider is { } tp) builder.Services.AddSingleton<TimeProvider>(tp);`
   This runs before `AddQuarkRuntime`'s `TryAddSingleton`, so the fake clock wins for the whole silo. `TestCluster` already fans `Options` out to every `TestSilo`, so a single option covers the cluster.

## Testing story

`Quark.Testing` itself does **not** reference `Microsoft.Extensions.Time.Testing` — it only exposes the `TimeProvider` slot. The test *project* adds the package and supplies `FakeTimeProvider`.

```csharp
using Microsoft.Extensions.Time.Testing;

var fakeTime = new FakeTimeProvider();
await using var cluster = await TestCluster.CreateAsync(options =>
{
    options.TimeProvider = fakeTime;                 // whole cluster uses the fake clock
    options.ConfigureSiloServices = services =>
    {
        services.AddGrainBehavior<ITickerGrain, TickerBehavior>();
        services.AddScoped<IActivationMemory<TickerState>>(sp =>
            new ActivationMemoryAccessor<TickerState>(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<TickerState>()));
    };
    options.ConfigureClientServices = services =>
        services.AddGrainProxy<ITickerGrain, TickerGrainProxy>();
});

var grain = cluster.Client.GetGrain<ITickerGrain>("t1");
await grain.StartTickingAsync(period: TimeSpan.FromSeconds(30));   // registers a periodic timer

fakeTime.Advance(TimeSpan.FromSeconds(30));                        // fire exactly once
// timer callback is posted to the grain mailbox; give it a turn to drain:
await grain.PingAsync();                                           // mailbox is serial -> ordering barrier
Assert.Equal(1, await grain.GetTickCountAsync());

fakeTime.Advance(TimeSpan.FromSeconds(90));                        // 3 more periods
await grain.PingAsync();
Assert.Equal(4, await grain.GetTickCountAsync());
```

Note on the mailbox hop: `GrainTimer.OnFire` posts the callback through `PostAsync` (the grain mailbox), it does not run inline on `Advance`. Tests must drain the mailbox after advancing — the simplest deterministic barrier is any subsequent grain call (the mailbox is single-reader/serial), as shown with `PingAsync()` above. Document this in the harness XML doc so authors don't race the assertion.

## AOT notes

Trivial. `TimeProvider` is an abstract BCL class and `TimeProvider.CreateTimer`/`ITimer` are ordinary virtual dispatch — no reflection, no `DynamicMethod`, no serialization, nothing that trips the trim/AOT analyzers. `FakeTimeProvider` lives only in the test assembly (never in a trimmed/published silo). No `Directory.Build.props` exemptions, no `[RequiresUnreferencedCode]` annotations. Nothing crosses a TCP boundary, so no `[GenerateSerializer]` is involved.

## Test plan

- **Unit (`Quark.Tests.Unit`)** — direct `GrainTimer<TState>` construction with a `FakeTimeProvider`:
  - one-shot timer (`Period = Timeout.InfiniteTimeSpan`) fires exactly once after `Advance(DueTime)`.
  - periodic timer fires N times after `Advance(N * Period)`.
  - non-interleave: a callback still in-flight when the next tick lands is skipped (drive via a callback that blocks on a `TaskCompletionSource`, then `Advance`).
  - `Change(dueTime, period)` reschedules against the fake clock.
  - `Dispose()` before `Advance` produces zero fires.
- **Integration (`Quark.Tests.Integration`)** — the `TestCluster` + `FakeTimeProvider` flow above: assert tick count after `Advance`, using a grain call as the mailbox-drain barrier. Add one test proving `options.TimeProvider == null` still uses real time (short real-delay sanity, kept minimal / `[Trait]`-guarded if flaky-prone).
- **Regression** — run existing `Quark.Tests.Unit` timer tests unchanged to confirm the optional-param defaults preserve `TimeProvider.System` behavior. Run the fault-test fixtures that `new` `GrainActivation`/`LocalGrainCallInvoker` directly to confirm they compile without edits.
- **AOT** — existing `dotnet publish ... /p:PublishAot=true` smoke build must stay green (expected: no new warnings).

## Implementation checklist

- [ ] Add `Microsoft.Extensions.Time.Testing` to `Directory.Packages.props` (test-only version pin).
- [ ] Reference `Microsoft.Extensions.Time.Testing` from `tests/Quark.Tests.Unit` and `tests/Quark.Tests.Integration` csproj (NOT from `src/Quark.Testing`).
- [ ] `GrainTimer.cs`: `Timer` field -> `ITimer`; add `TimeProvider timeProvider` ctor param; use `timeProvider.CreateTimer(...)`.
- [ ] `GrainActivation.cs`: add optional `TimeProvider? timeProvider = null` ctor param + `_timeProvider` field (`?? TimeProvider.System`); pass to `new GrainTimer<TState>` at line 211.
- [ ] `LocalGrainCallInvoker.cs`: add optional `TimeProvider? timeProvider = null` ctor param + field; pass to `new GrainActivation(...)` at line 285.
- [ ] `RuntimeServiceCollectionExtensions.cs`: `TryAddSingleton<TimeProvider>(TimeProvider.System)`; wire `sp.GetService<TimeProvider>()` into the `LocalGrainCallInvoker` factory.
- [ ] `TestClusterOptions.cs`: add `public TimeProvider? TimeProvider { get; set; }`.
- [ ] `TestSilo.cs`: register `Options.TimeProvider` as `AddSingleton<TimeProvider>` before `ConfigureSiloServices` runs.
- [ ] Unit tests for `GrainTimer<TState>` against `FakeTimeProvider` (one-shot, periodic, non-interleave, Change, Dispose).
- [ ] Integration test: `TestCluster` + `FakeTimeProvider.Advance` drives a periodic grain timer.
- [ ] XML doc on `TestClusterOptions.TimeProvider` noting the mailbox-drain barrier requirement.
- [ ] Update `wiki/` timers section + `quark-host-setup` / `quark-testing` skill snippets to mention deterministic timer testing.

## Resolved design decisions

- **DI silo-wide singleton, not a per-timer option.** A `TimeProvider` on `GrainTimerCreationOptions` would (a) break drop-in parity with Orleans' options type, (b) force every call site to thread a clock, and (c) split the silo's notion of time per timer, which has no real use case. One injected clock per silo is what `FakeTimeProvider` is designed for. No per-call override.
- **`TryAddSingleton(TimeProvider.System)` default.** Guarantees existing hosts are byte-for-byte unchanged and lets any host (or TestSilo) override by registering first.
- **Optional trailing constructor params, defaulting to `TimeProvider.System`.** Keeps hand-written test proxies and the `Quark.Tests.Fault*` fixtures (which construct `GrainActivation` / `LocalGrainCallInvoker` directly) compiling with no edits.
- **Idle collection clock deferred to a separate issue.** Confirmed the collector and activation last-access clocking are a distinct 4-site surface; folding them in would enlarge this well-scoped change and risk the idle-timeout tests. Called out as a follow-up.
- **`Quark.Testing` stays free of `Microsoft.Extensions.Time.Testing`.** The harness exposes only the abstract `TimeProvider` slot; the concrete fake is a test-assembly concern, keeping the shipped testing package lean.
