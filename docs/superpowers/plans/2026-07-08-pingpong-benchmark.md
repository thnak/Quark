# Ping-Pong Throughput Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `PingPong` benchmark scenario to `tests/Quark.Performance` that exercises Quark's real
grain-to-grain dispatch path (`IGrainCallInvoker` → `GrainActivationTable` → mailbox) with trivial,
near-zero-work messages, and reports sustained msg/s — comparable to the classic Akka(.NET) ping-pong
benchmark.

**Architecture:** A stateless `PingPongGrainBehavior` does nothing but return. K independent pairs
(`ping-{i}`/`pong-{i}` grain instances) each run their own tight loop on a dedicated `Task.Run`, alternating
which grain in the pair they call. A driver (`PingPongRunner`) spawns the pairs, runs them for a configured
duration against a single in-process `TestCluster` silo, and reports msg/s from the same
`BenchmarkDiagnosticListener` AstroSim already built (`IQuarkDiagnosticListener` counting
`OnInvocationEnd`), reported at both the raw grain-call rate and a ×2 rate approximating Akka's
two-messages-per-round-trip counting convention.

**Tech Stack:** .NET 10, Quark.Runtime (`LocalGrainCallInvoker`), Quark.Testing `TestCluster`, hand-written
grain proxy/invokables (house convention for this project, matches AstroSim), reused
`Quark.Performance.AstroSim.BenchmarkDiagnosticListener`.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md` — every task below implements one
  section of it.
- Lives entirely in `tests/Quark.Performance/PingPong/` (new subfolder) + one edit to
  `tests/Quark.Performance/Program.cs`. No `.csproj` changes needed — all required project references
  (`Quark.Persistence.Abstractions`, `Quark.Diagnostics`, `Quark.Diagnostics.Abstractions`) were already
  added for AstroSim.
- No `[Reentrant]` anywhere — no grain in this design ever calls another grain (see spec §4). Both `ping-{i}`
  and `pong-{i}` are passive receivers; only the driver's per-pair loop initiates calls.
- Single in-process silo: `TestClusterOptions.InitialSilosCount = 1` (harness default is 2 — must be set
  explicitly, same gotcha as AstroSim).
- Diagnostic listener MUST be registered via `services.AddSingleton<IQuarkDiagnosticListener>(listener)` —
  **never** `services.AddQuarkDiagnostics(listener)` (confirmed circular-DI bug that hangs silo startup
  forever — see AstroSim spec §5 and memory `project_quark_diagnostics_circular_bug`).
- Real parallelism requires each pair's loop to run via `Task.Run` — trivial in-process grain calls complete
  synchronously, so without this every pair collapses onto one thread (the exact bug fixed post-ship in
  AstroSim's tick loop).
- No new automated tests — matches `LocalStreamingTest`/AstroSim precedent (headless perf harness). Validation
  is a manual run (Task 4).
- `dotnet build tests/Quark.Performance/Quark.Performance.csproj` must succeed after every task.

---

## File structure

```
tests/Quark.Performance/
  PingPong/
    IPingPongGrain.cs            — Task 1: grain contract
    PingPongGrainBehavior.cs     — Task 1: stateless no-op behavior
    PingPongGrainInvokables.cs   — Task 2: hand-written IGrainVoidInvokable
    PingPongGrainProxy.cs        — Task 2: hand-written grain proxy
    PingPongRunner.cs            — Task 3: CLI args, DI wiring, pair spawning, reporting
  Program.cs                     — Task 3: add "PingPong" subcommand dispatch
```

---

### Task 1: Grain contract + stateless behavior

**Files:**
- Create: `tests/Quark.Performance/PingPong/IPingPongGrain.cs`
- Create: `tests/Quark.Performance/PingPong/PingPongGrainBehavior.cs`

**Interfaces:**
- Produces: `IPingPongGrain` (method `PingAsync()`), `PingPongGrainBehavior` (no constructor dependencies —
  stateless) — all in namespace `Quark.Performance.PingPong`. Task 2's invokable casts to `IPingPongGrain`;
  Task 3's DI wiring registers `PingPongGrainBehavior` via `AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>()`.

- [ ] **Step 1: Create the grain contract file**

Create `tests/Quark.Performance/PingPong/IPingPongGrain.cs`:

```csharp
using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.PingPong;

public interface IPingPongGrain : IGrainWithStringKey
{
    ValueTask PingAsync();
}
```

- [ ] **Step 2: Create the behavior file**

Create `tests/Quark.Performance/PingPong/PingPongGrainBehavior.cs`:

```csharp
using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.PingPong;

public sealed class PingPongGrainBehavior : IGrainBehavior, IPingPongGrain
{
    public ValueTask PingAsync() => default;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds (0 errors). Nothing constructs `PingPongGrainBehavior` yet — that's fine.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Performance/PingPong/IPingPongGrain.cs tests/Quark.Performance/PingPong/PingPongGrainBehavior.cs
git commit -m "pingpong: add grain contract and stateless behavior"
```

---

### Task 2: Hand-written invokable + proxy

**Files:**
- Create: `tests/Quark.Performance/PingPong/PingPongGrainInvokables.cs`
- Create: `tests/Quark.Performance/PingPong/PingPongGrainProxy.cs`

**Interfaces:**
- Consumes: `IPingPongGrain` (Task 1), `IGrainVoidInvokable`/`IGrainProxyActivator<TSelf>`/`IGrainCallInvoker`
  (`Quark.Core.Abstractions.Hosting`, existing).
- Produces: `PingPongBehavior_PingInvokable` (`MethodId` 0), `PingPongGrainProxy` — Task 3's DI wiring
  registers the proxy via `services.AddGrainProxy<IPingPongGrain, PingPongGrainProxy>()`.

- [ ] **Step 1: Create the invokable file**

Create `tests/Quark.Performance/PingPong/PingPongGrainInvokables.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance.PingPong;

internal readonly struct PingPongBehavior_PingInvokable : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IPingPongGrain)behavior).PingAsync();
    public void Serialize(ref CodecWriter writer) { }
}
```

- [ ] **Step 2: Create the proxy file**

Create `tests/Quark.Performance/PingPong/PingPongGrainProxy.cs`:

```csharp
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Performance.PingPong;

public sealed class PingPongGrainProxy : IPingPongGrain, IGrainProxyActivator<PingPongGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public PingPongGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static PingPongGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public ValueTask PingAsync()
        => _invoker.InvokeVoidAsync(_grainId, new PingPongBehavior_PingInvokable());
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Performance/PingPong/PingPongGrainInvokables.cs tests/Quark.Performance/PingPong/PingPongGrainProxy.cs
git commit -m "pingpong: add hand-written grain invokable and proxy"
```

---

### Task 3: Driver — DI wiring, pair spawning, reporting, Program.cs subcommand

**Files:**
- Create: `tests/Quark.Performance/PingPong/PingPongRunner.cs`
- Modify: `tests/Quark.Performance/Program.cs`

**Interfaces:**
- Consumes: `IPingPongGrain`, `PingPongGrainBehavior` (Task 1), `PingPongGrainProxy` (Task 2), plus existing
  types: `TestCluster` (`Quark.Testing.Harness`), `AddQuarkRuntime`/`AddGrainBehavior` (`Quark.Runtime`),
  `AddLocalClusterClient`/`AddGrainProxy` (`Quark.Client`), `IQuarkDiagnosticListener`
  (`Quark.Diagnostics.Abstractions`), and **`BenchmarkDiagnosticListener`** — reused directly from
  `Quark.Performance.AstroSim` (already built, counts `OnInvocationEnd` via `Interlocked.Increment`, exposes
  `public long Count`). Do not create a second copy of this class.
- Produces: `PingPongRunner.RunAsync(string[] args)` — called from `Program.cs`.

- [ ] **Step 1: Create the runner file**

**This exact code was built and run end-to-end during design verification** (2026-07-08): a smoke run at
`--pairs 4 --duration 3` and a default-scale run at `--pairs 32 --duration 15` (32-core machine), both clean
— no exceptions, sane throughput figures, and independently confirmed via `pidstat` to show 20+ distinct
`.NET TP Worker` threads each independently busy (real multi-core parallelism, not the single-thread
collapse AstroSim's tick loop originally had before its `Task.Run` fix).

Create `tests/Quark.Performance/PingPong/PingPongRunner.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Diagnostics.Abstractions;
using Quark.Performance.AstroSim;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.PingPong;

public static class PingPongRunner
{
    public static async Task RunAsync(string[] args)
    {
        PingPongCliArgs cli = PingPongCliArgs.Parse(args);
        var listener = new BenchmarkDiagnosticListener();

        Console.WriteLine("=== Ping-Pong Throughput Benchmark ===");
        Console.WriteLine($"  Pairs: {cli.Pairs}, Duration: {cli.DurationSeconds}s");
        Console.WriteLine("  Note: reported msg/s is 2x the raw grain-call count, approximating Akka's");
        Console.WriteLine("  one-way-tell convention (ping leg + pong leg per round trip).\n");

        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                // NOT services.AddQuarkDiagnostics(listener) -- confirmed circular-DI bug, see
                // docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md section 5.
                services.AddSingleton<IQuarkDiagnosticListener>(listener);
                services.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IPingPongGrain, PingPongGrainProxy>();
            };
        });

        var pairs = new (IPingPongGrain ping, IPingPongGrain pong)[cli.Pairs];
        for (int i = 0; i < cli.Pairs; i++)
        {
            pairs[i] = (
                cluster.Client.GetGrain<IPingPongGrain>($"ping-{i}"),
                cluster.Client.GetGrain<IPingPongGrain>($"pong-{i}"));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));
        long startCount = listener.Count;
        var totalSw = Stopwatch.StartNew();

        var pairTasks = new Task[cli.Pairs];
        for (int i = 0; i < cli.Pairs; i++)
        {
            (IPingPongGrain ping, IPingPongGrain pong) = pairs[i];
            pairTasks[i] = Task.Run(() => RunPairAsync(ping, pong, cts.Token));
        }

        Task reportTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1000);
                long delta = listener.Count - startCount;
                double elapsed = totalSw.Elapsed.TotalSeconds;
                if (elapsed > 0)
                {
                    Console.WriteLine($"  t={elapsed:F0}s  {2 * delta / elapsed:N0} msg/s (x2, cumulative avg)");
                }
            }
        });

        await Task.WhenAll(pairTasks);
        totalSw.Stop();
        await reportTask;

        long totalCalls = listener.Count - startCount;
        double totalSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine("=== Ping-Pong Complete ===");
        Console.WriteLine($"  Pairs: {cli.Pairs}");
        Console.WriteLine($"  Duration: {totalSeconds:F1}s");
        Console.WriteLine($"  Raw grain calls: {totalCalls:N0}");
        Console.WriteLine($"  Raw call rate: {totalCalls / totalSeconds:N0} calls/s");
        Console.WriteLine($"  Akka-comparable rate (x2): {2 * totalCalls / totalSeconds:N0} msg/s");
    }

    private static async Task RunPairAsync(IPingPongGrain ping, IPingPongGrain pong, CancellationToken ct)
    {
        IPingPongGrain[] targets = [pong, ping];
        long i = 0;
        while (!ct.IsCancellationRequested)
        {
            await targets[i % 2].PingAsync();
            i++;
        }
    }
}

internal sealed class PingPongCliArgs
{
    public int Pairs { get; private init; } = Environment.ProcessorCount;
    public double DurationSeconds { get; private init; } = 10;

    public static PingPongCliArgs Parse(string[] args)
    {
        int pairs = Environment.ProcessorCount;
        double duration = 10;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pairs" when i + 1 < args.Length:
                    pairs = int.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
            }
        }

        return new PingPongCliArgs { Pairs = pairs, DurationSeconds = duration };
    }
}
```

- [ ] **Step 2: Wire the subcommand into Program.cs**

In `tests/Quark.Performance/Program.cs`, add a `using Quark.Performance.PingPong;` and a third subcommand
check right after the existing `AstroSim` block:

```csharp
using BenchmarkDotNet.Running;
using Quark.Performance.AstroSim;
using Quark.Performance.PingPong;

namespace Quark.Performance;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Check if user wants to run local streaming test
        if (args.Length > 0 && args[0].Equals("LocalStreaming", StringComparison.OrdinalIgnoreCase))
        {
            await LocalStreamingTest.RunAsync();
            return;
        }

        // Check if user wants to run the astro-sim throughput benchmark
        if (args.Length > 0 && args[0].Equals("AstroSim", StringComparison.OrdinalIgnoreCase))
        {
            await AstroSimRunner.RunAsync(args);
            return;
        }

        // Check if user wants to run the ping-pong throughput benchmark
        if (args.Length > 0 && args[0].Equals("PingPong", StringComparison.OrdinalIgnoreCase))
        {
            await PingPongRunner.RunAsync(args);
            return;
        }

        // Otherwise run BenchmarkDotNet benchmarks
        var switcher = new BenchmarkSwitcher(new[]
        {
            typeof(GrainCallBenchmarks),
            typeof(StreamingBenchmarks),
            typeof(SerializationBenchmarks),
        });

        switcher.Run(args);
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Performance/PingPong/PingPongRunner.cs tests/Quark.Performance/Program.cs
git commit -m "pingpong: add CLI driver, DI wiring, and Program.cs subcommand"
```

---

### Task 4: Manual validation run

**Files:** none (execution only).

**Already done once, by hand, while writing this plan** (2026-07-08) — Tasks 1–3's exact code was built and
run end-to-end against a live silo at `--pairs 4 --duration 3` and at default settings
(`--pairs 32 --duration 15`, on a 32-core machine): no exceptions, sane throughput, and `pidstat` confirmed
20+ distinct `.NET TP Worker` threads independently busy (genuine multi-core parallelism). Re-run it here to
confirm on your own checkout.

- [ ] **Step 1: Small-scale smoke run**

Run: `dotnet run --project tests/Quark.Performance -- PingPong --pairs 4 --duration 3`

Expected: No exceptions or stack traces. Console prints the pairs/duration header, one or more rolling
`msg/s (x2, cumulative avg)` lines, and a final `=== Ping-Pong Complete ===` block with nonzero `Raw grain
calls`, `Raw call rate`, and `Akka-comparable rate (x2)` (exactly double the raw rate).

Reference output from design verification:
```
=== Ping-Pong Complete ===
  Pairs: 4
  Duration: 3.0s
  Raw grain calls: 212,520
  Raw call rate: 70,830 calls/s
  Akka-comparable rate (x2): 141,660 msg/s
```

- [ ] **Step 2: Default-scale run**

Run: `dotnet run -c Release --project tests/Quark.Performance -- PingPong`

(Uses `--pairs {Environment.ProcessorCount}` and `--duration 10`.) Expected: same clean shape, no
exceptions, `Akka-comparable rate (x2)` exactly double `Raw call rate`.

Reference output from design verification (32-core machine, `--duration 15` variant — expect a similar
order of magnitude, not an exact match, since core count and run duration affect the figure):
```
=== Ping-Pong Complete ===
  Pairs: 32
  Duration: 15.0s
  Raw grain calls: 5,673,367
  Raw call rate: 378,149 calls/s
  Akka-comparable rate (x2): 756,297 msg/s
```

- [ ] **Step 3: Report the result**

Report the default-scale run's `Akka-comparable rate (x2)` figure next to:
- AstroSim's sustained throughput (203,832 msg/s at 10M bodies/32³ grid, post-`Task.Run`-fix, per
  `docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md`),
- `GrainCallBenchmarks.CounterIncrement`'s single-threaded rate (~27M ops/sec, i.e. 36.77ns/op — NOT a real
  grain-dispatch number, see this plan's spec §1),
- Akka's cited ~50M msg/s local ping-pong figure,

in the PR description or commit message for this task — no code changes, so nothing to commit for this step.
Note plainly that PingPong's number and Akka's are still not a strict apples-to-apples comparison (different
runtime, different hardware, different message-passing primitive — RPC/ask vs. one-way tell, see spec §5),
but it is the first Quark benchmark that actually measures the real dispatch path with trivial messages.
