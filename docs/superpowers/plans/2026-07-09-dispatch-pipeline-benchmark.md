# Dispatch Pipeline Benchmark Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `tests/Quark.Performance/DispatchPipelineBenchmarks.cs`, a BenchmarkDotNet suite that isolates
each stage of `LocalGrainCallInvoker`'s call path (activation table lookup, `IServiceScope` creation, DI
behavior resolution, `ExecutionContext.Capture()`, mailbox/channel round trip with and without the scheduler,
diagnostics on/off, and the RPC-await-vs-no-wait pattern) so the ~66x gap to Akka's ping-pong figure can be
attributed to specific stages.

**Architecture:** Each benchmark builds its own minimal `ServiceProvider` via `AddQuarkRuntime()` (no full
`TestCluster`/silo networking — same hand-wired pattern `Quark.Tests.Fault/FaultFixture.cs` uses), reusing
`Quark.Performance.PingPong.IPingPongGrain`/`PingPongGrainBehavior` as the zero-work payload throughout. Bare
`GrainActivation` instances are constructed directly (bypassing `GrainActivationTable`) for the mailbox-only
stages so channel/scheduler cost is isolated from DI/behavior-resolution cost.

**Tech Stack:** .NET 10, BenchmarkDotNet, Quark.Runtime (`LocalGrainCallInvoker`, `GrainActivation`,
`GrainActivationTable`, `GrainScopeBinder`), Microsoft.Extensions.DependencyInjection.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md` — every task below
  implements one section of it.
- Lives entirely in one new file, `tests/Quark.Performance/DispatchPipelineBenchmarks.cs`, plus one
  `.csproj` edit (`src/Quark.Runtime/Quark.Runtime.csproj`).
- `dotnet build tests/Quark.Performance/Quark.Performance.csproj` must succeed after every task.
- No new automated tests — matches `GrainCallBenchmarks`/`PingPong`/`AstroSim` precedent.
- Namespace: `Quark.Performance` (flat, matching `GrainCallBenchmarks.cs`/`SerializationBenchmarks.cs`, not
  a subfolder like `PingPong`/`AstroSim`).

---

## File structure

```
src/Quark.Runtime/
  Quark.Runtime.csproj                     — Task 1: add InternalsVisibleTo Quark.Performance
tests/Quark.Performance/
  DispatchPipelineBenchmarks.cs            — Tasks 2-6: all stage benchmarks
```

---

### Task 1: `InternalsVisibleTo` for `Quark.Performance`

**Files:**
- Modify: `src/Quark.Runtime/Quark.Runtime.csproj`

**Interfaces:**
- Produces: `Quark.Performance` gains access to `Quark.Runtime` internals (`GrainScopeBinder`,
  `GrainActivation`'s internal members) — Task 3 depends on this for `GrainScopeBinder.BindAndResolveAsync`.

- [ ] **Step 1: Add the InternalsVisibleTo entry**

In `src/Quark.Runtime/Quark.Runtime.csproj`, find the existing `<InternalsVisibleTo>` block:

```xml
    <InternalsVisibleTo Include="Quark.Tests.Unit"/>
    <InternalsVisibleTo Include="Quark.Tests.Fault"/>
    <InternalsVisibleTo Include="Quark.Tests.Integration"/>
    <InternalsVisibleTo Include="Quark.Diagnostics"/>
```

Add one line so it reads:

```xml
    <InternalsVisibleTo Include="Quark.Tests.Unit"/>
    <InternalsVisibleTo Include="Quark.Tests.Fault"/>
    <InternalsVisibleTo Include="Quark.Tests.Integration"/>
    <InternalsVisibleTo Include="Quark.Diagnostics"/>
    <InternalsVisibleTo Include="Quark.Performance"/>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Quark.Runtime/Quark.Runtime.csproj`
Expected: Build succeeds (0 errors).

- [ ] **Step 3: Commit**

```bash
git add src/Quark.Runtime/Quark.Runtime.csproj
git commit -m "runtime: expose internals to Quark.Performance for dispatch-pipeline benchmarks"
```

---

### Task 2: Benchmark class skeleton + stages 1-4 (table lookup, scope create, scope bind+resolve, ExecutionContext capture)

**Files:**
- Create: `tests/Quark.Performance/DispatchPipelineBenchmarks.cs`

**Interfaces:**
- Consumes: `Quark.Performance.PingPong.IPingPongGrain`/`PingPongGrainBehavior`/`PingPongBehavior_PingInvokable`
  (existing, from the PingPong benchmark), `Quark.Runtime.GrainActivationTable`, `Quark.Runtime.GrainTypeRegistry`,
  `Quark.Runtime.GrainScopeBinder` (internal, Task 1), `Quark.Core.Abstractions.Hosting.IGrainCallInvoker`,
  `Quark.Core.Abstractions.Identity.GrainId`/`GrainType`.
- Produces: `DispatchPipelineBenchmarks` class with `[GlobalSetup]`/`[GlobalCleanup]` and 4 `[Benchmark]`
  methods. Tasks 3-6 add more `[Benchmark]` methods and setup fields to this same class.

- [ ] **Step 1: Create the file with setup/cleanup and the first four benchmarks**

Create `tests/Quark.Performance/DispatchPipelineBenchmarks.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quark.Core.Abstractions.Identity;
using Quark.Performance.PingPong;
using Quark.Runtime;
using Quark.Serialization;

namespace Quark.Performance;

/// <summary>
/// Isolates each stage of LocalGrainCallInvoker's dispatch path so the gap to Akka's ping-pong
/// figure (see docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md) can be
/// attributed to specific stages instead of guessed at.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class DispatchPipelineBenchmarks
{
    private static readonly GrainType PingPongGrainType = new("PingPongGrain");
    private static readonly Func<ValueTask<GrainActivation>> ThrowingFactory =
        static () => throw new InvalidOperationException("Factory should not run — activation must already exist.");

    private ServiceProvider _sp = null!;
    private GrainActivationTable _activationTable = null!;
    private IGrainCallInvoker _invoker = null!;
    private GrainId _grainId;
    private GrainActivation _bareActivation = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkSerialization();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "bench";
            o.ServiceId = "bench";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
        _sp = services.BuildServiceProvider();

        _sp.GetRequiredService<GrainTypeRegistry>().Register(PingPongGrainType, typeof(PingPongGrainBehavior));

        _activationTable = _sp.GetRequiredService<GrainActivationTable>();
        _invoker = _sp.GetRequiredService<IGrainCallInvoker>();
        _grainId = GrainId.Create(PingPongGrainType, "bench-0");

        // Pre-activate so the table-lookup and full-invoke benchmarks hit steady state.
        await _invoker.InvokeVoidAsync(_grainId, new PingPongBehavior_PingInvokable());

        // Bare activation, constructed directly (bypassing GrainActivationTable) so mailbox-only
        // stages (Task 3) measure the channel/scheduler cost in isolation from DI/behavior resolution.
        _bareActivation = new GrainActivation(
            GrainId.Create(PingPongGrainType, "bare-non-reentrant"),
            PingPongGrainType,
            isReentrant: false,
            _sp,
            _sp.GetRequiredService<ILogger<GrainActivation>>());
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _bareActivation.DisposeAsync();
        await _activationTable.DisposeAsync();
        await _sp.DisposeAsync();
    }

    // Stage 1: GrainActivationTable.GetOrCreateAsync steady-state lookup.
    [Benchmark]
    public async ValueTask<GrainActivation> ActivationTableLookup()
        => await _activationTable.GetOrCreateAsync(_grainId, ThrowingFactory);

    // Stage 2: bare IServiceScope create+dispose, no resolution.
    [Benchmark]
    public void ServiceScopeCreateDispose()
    {
        using IServiceScope scope = _sp.CreateScope();
    }

    // Stage 3: GrainScopeBinder.BindAndResolveAsync — the 4 DI resolutions + behavior resolve.
    [Benchmark]
    public async ValueTask<Quark.Core.Abstractions.Grains.IGrainBehavior> ScopeBindAndResolve()
    {
        using IServiceScope scope = _sp.CreateScope();
        return await GrainScopeBinder.BindAndResolveAsync(scope.ServiceProvider, _bareActivation, default);
    }

    // Stage 4: ExecutionContext.Capture() alone (called on every PostAsync).
    [Benchmark]
    public System.Threading.ExecutionContext? ExecutionContextCapture()
        => System.Threading.ExecutionContext.Capture();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds (0 errors). `GrainScopeBinder` must resolve — if it doesn't, Task 1 wasn't
applied correctly.

- [ ] **Step 3: Register the class with Program.cs's BenchmarkSwitcher**

In `tests/Quark.Performance/Program.cs`, add `typeof(DispatchPipelineBenchmarks)` to the `switcher` array
(otherwise `--filter` finds 0 benchmarks — this class is invoked by name, not by namespace scanning):

```csharp
        var switcher = new BenchmarkSwitcher(new[]
        {
            typeof(GrainCallBenchmarks),
            typeof(StreamingBenchmarks),
            typeof(SerializationBenchmarks),
            typeof(DispatchPipelineBenchmarks),
        });
```

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Performance/DispatchPipelineBenchmarks.cs tests/Quark.Performance/Program.cs
git commit -m "dispatch-pipeline-benchmarks: add harness + table lookup, scope, and ExecutionContext stages"
```

---

### Task 3: Stages 5-6 — mailbox round trip, non-reentrant vs reentrant

**Files:**
- Modify: `tests/Quark.Performance/DispatchPipelineBenchmarks.cs`

**Interfaces:**
- Consumes: `_sp`, `GrainActivation` (Task 2's setup), `Quark.Core.Abstractions.Identity.GrainId`/`GrainType`.
- Produces: `_bareActivationReentrant` field; `MailboxRoundTrip`/`MailboxRoundTripReentrant` benchmarks.

- [ ] **Step 1: Add the reentrant bare activation field and its setup/cleanup**

In `DispatchPipelineBenchmarks.cs`, add a field next to `_bareActivation`:

```csharp
    private GrainActivation _bareActivation = null!;
    private GrainActivation _bareActivationReentrant = null!;
```

In `Setup()`, right after the existing `_bareActivation = new GrainActivation(...)` block, add:

```csharp
        _bareActivationReentrant = new GrainActivation(
            GrainId.Create(PingPongGrainType, "bare-reentrant"),
            PingPongGrainType,
            isReentrant: true,
            _sp,
            _sp.GetRequiredService<ILogger<GrainActivation>>());
```

In `Cleanup()`, add before the existing `await _bareActivation.DisposeAsync();` line:

```csharp
        await _bareActivationReentrant.DisposeAsync();
```

- [ ] **Step 2: Add the two mailbox benchmarks**

Add a static no-op work item field near `ThrowingFactory`:

```csharp
    private static readonly Func<ValueTask> NoOpWorkItem = static () => default;
```

Add the benchmarks after `ExecutionContextCapture`:

```csharp
    // Stage 5: mailbox round trip, non-reentrant — real Channel write + scheduler wake +
    // pooled-item completion signal (forced onto the thread pool).
    [Benchmark]
    public async ValueTask MailboxRoundTrip()
        => await _bareActivation.PostAsync(NoOpWorkItem);

    // Stage 6: same call, but isReentrant: true — bypasses the channel/scheduler entirely
    // (inline execution). The delta vs MailboxRoundTrip is the channel+scheduler+thread-hop cost.
    [Benchmark]
    public async ValueTask MailboxRoundTripReentrant()
        => await _bareActivationReentrant.PostAsync(NoOpWorkItem);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Performance/DispatchPipelineBenchmarks.cs
git commit -m "dispatch-pipeline-benchmarks: add mailbox round-trip stages (reentrant vs non-reentrant)"
```

---

### Task 4: Stages 7-8 — full invoke, diagnostics off vs on

**Files:**
- Modify: `tests/Quark.Performance/DispatchPipelineBenchmarks.cs`

**Interfaces:**
- Consumes: `Quark.Diagnostics.Abstractions.IQuarkDiagnosticListener`, `InvocationStartEvent`,
  `InvocationEndEvent` (all existing).
- Produces: `_spDiag`/`_invokerDiag`/`_grainIdDiag` fields, a private `CountingDiagnosticListener` nested
  class, and `FullInvokeDiagnosticsOff`/`FullInvokeDiagnosticsOn`/`FullInvokeVoidAsync` benchmarks.

- [ ] **Step 1: Add the diagnostics-enabled DI root**

Add `using Quark.Diagnostics.Abstractions;` to the top of the file.

Add fields next to `_bareActivationReentrant`:

```csharp
    private ServiceProvider _spDiag = null!;
    private IGrainCallInvoker _invokerDiag = null!;
    private GrainId _grainIdDiag;
```

In `Setup()`, after the existing `_bareActivationReentrant = new GrainActivation(...)` block, add:

```csharp
        var diagServices = new ServiceCollection();
        diagServices.AddLogging();
        diagServices.AddQuarkSerialization();
        diagServices.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "bench-diag";
            o.ServiceId = "bench-diag";
            o.SiloName = "silo0";
        });
        diagServices.AddSingleton<IQuarkDiagnosticListener, CountingDiagnosticListener>();
        diagServices.AddQuarkRuntime();
        diagServices.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
        _spDiag = diagServices.BuildServiceProvider();
        _spDiag.GetRequiredService<GrainTypeRegistry>().Register(PingPongGrainType, typeof(PingPongGrainBehavior));
        _invokerDiag = _spDiag.GetRequiredService<IGrainCallInvoker>();
        _grainIdDiag = GrainId.Create(PingPongGrainType, "bench-diag-0");
        await _invokerDiag.InvokeVoidAsync(_grainIdDiag, new PingPongBehavior_PingInvokable());
```

In `Cleanup()`, add before `await _activationTable.DisposeAsync();`:

```csharp
        await _spDiag.GetRequiredService<GrainActivationTable>().DisposeAsync();
```

And after `await _sp.DisposeAsync();`, add:

```csharp
        await _spDiag.DisposeAsync();
```

- [ ] **Step 2: Add the CountingDiagnosticListener and the three benchmarks**

Add a private nested class at the bottom of `DispatchPipelineBenchmarks` (inside the class body, after the
benchmark methods):

```csharp
    private sealed class CountingDiagnosticListener : IQuarkDiagnosticListener
    {
        private long _count;
        public void OnInvocationStart(in InvocationStartEvent e) => Interlocked.Increment(ref _count);
        public void OnInvocationEnd(in InvocationEndEvent e) => Interlocked.Increment(ref _count);
    }
```

Add the benchmarks after `MailboxRoundTripReentrant`:

```csharp
    // Stage 7a: complete InvokeVoidAsync, NullDiagnosticListener (default — no-op).
    [Benchmark]
    public async ValueTask FullInvokeDiagnosticsOff()
        => await _invoker.InvokeVoidAsync(_grainId, new PingPongBehavior_PingInvokable());

    // Stage 7b: same call, with a listener that does real work (Interlocked.Increment), not a no-op.
    [Benchmark]
    public async ValueTask FullInvokeDiagnosticsOn()
        => await _invokerDiag.InvokeVoidAsync(_grainIdDiag, new PingPongBehavior_PingInvokable());

    // Stage 8: the complete real number, single-threaded ops/sec — should roughly equal
    // stage 1 + stage 3 + stage 5 combined. Distinctly named per the design's summary table.
    [Benchmark]
    public async ValueTask FullInvokeVoidAsync()
        => await _invoker.InvokeVoidAsync(_grainId, new PingPongBehavior_PingInvokable());
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Performance/DispatchPipelineBenchmarks.cs
git commit -m "dispatch-pipeline-benchmarks: add full-invoke stages with diagnostics on/off"
```

---

### Task 5: Stage 9 — channel-signal vs channel-no-signal pattern

**Files:**
- Modify: `tests/Quark.Performance/DispatchPipelineBenchmarks.cs`

**Interfaces:**
- Consumes: `System.Threading.Channels.Channel<T>` (BCL).
- Produces: `ChannelSignalPattern`/`ChannelNoSignalPattern` benchmarks.

Not a call into production fire-and-forget code — Quark has none (see spec §2). This is a synthetic
`Channel<T>` harness shaped like `GrainActivation`'s real queue (unbounded, `SingleReader = true`,
`AllowSynchronousContinuations = false`), comparing "write + await a forced-async completion signal"
(Quark's real RPC-await pattern) against "write and return immediately" (a hypothetical tell-style pattern).

- [ ] **Step 1: Add the channel fields and background reader setup**

Add `using System.Threading.Channels;` to the top of the file.

Add fields:

```csharp
    private Channel<TaskCompletionSource> _signalChannel = null!;
    private Channel<byte> _noSignalChannel = null!;
    private CancellationTokenSource _readerCts = null!;
    private Task _signalReaderTask = null!;
    private Task _noSignalReaderTask = null!;
```

In `Setup()`, after the `await _invokerDiag.InvokeVoidAsync(...)` line, add:

```csharp
        _readerCts = new CancellationTokenSource();
        _signalChannel = Channel.CreateUnbounded<TaskCompletionSource>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        _noSignalChannel = Channel.CreateUnbounded<byte>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        _signalReaderTask = Task.Run(() => DrainSignalChannelAsync(_readerCts.Token));
        _noSignalReaderTask = Task.Run(() => DrainNoSignalChannelAsync(_readerCts.Token));
```

- [ ] **Step 2: Add the drain loops and benchmarks**

Add private methods (after `Cleanup`):

```csharp
    private async Task DrainSignalChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (TaskCompletionSource tcs in _signalChannel.Reader.ReadAllAsync(ct))
            {
                tcs.SetResult();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainNoSignalChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (byte _ in _noSignalChannel.Reader.ReadAllAsync(ct))
            {
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
```

Add the benchmarks after `FullInvokeVoidAsync`:

```csharp
    // Stage 9a: write + await a forced-async completion signal — Quark's real RPC-await pattern.
    [Benchmark]
    public async Task ChannelSignalPattern()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _signalChannel.Writer.WriteAsync(tcs);
        await tcs.Task;
    }

    // Stage 9b: write and return immediately — no completion wait.
    [Benchmark]
    public async ValueTask ChannelNoSignalPattern()
        => await _noSignalChannel.Writer.WriteAsync(0);
```

- [ ] **Step 3: Update Cleanup to stop the reader loops**

In `Cleanup()`, before `await _bareActivationReentrant.DisposeAsync();`, add:

```csharp
        await _readerCts.CancelAsync();
        _signalChannel.Writer.TryComplete();
        _noSignalChannel.Writer.TryComplete();
        await _signalReaderTask;
        await _noSignalReaderTask;
        _readerCts.Dispose();
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add tests/Quark.Performance/DispatchPipelineBenchmarks.cs
git commit -m "dispatch-pipeline-benchmarks: add channel-signal vs no-signal pattern stage"
```

---

### Task 6: Run the suite and record findings

**Files:**
- Modify: `docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md` (append §7 Findings)

- [ ] **Step 1: Run the full benchmark suite**

Run: `dotnet run -c Release --project tests/Quark.Performance -- --filter '*DispatchPipeline*'`

Expected: BenchmarkDotNet runs all 11 `[Benchmark]` methods cleanly (no exceptions) and prints a summary
table with Mean and Allocated columns.

- [ ] **Step 2: Append the findings table to the spec**

Add a `## 7. Findings` section to
`docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md` with:
- The full BenchmarkDotNet results table (stage, mean ns/op, allocated bytes), sorted by mean descending.
- One paragraph identifying which stage(s) dominate — compare against PingPong's ~756K msg/s (32 pairs) and
  the ~66x Akka gap.
- One sentence on whether stage 1 + stage 3 + stage 5 roughly reconcile with stage 8 (full path); note any
  mismatch as its own finding (possible contention effects invisible in single-threaded microbenchmarks).
- One sentence stating explicitly whether this data changes the "not a serialization story" conclusion from
  §3 (it shouldn't — no stage here touches `CodecWriter`/`Serialize` — but confirm the numbers are
  consistent with that claim).

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md
git commit -m "dispatch-pipeline-benchmarks: record findings from the first suite run"
```
