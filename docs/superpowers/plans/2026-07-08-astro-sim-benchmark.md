# Astro-sim Throughput Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an `AstroSim` benchmark scenario to `tests/Quark.Performance` that drives 10M+ bodies through a 3D spatial-chunk grid of grains, entirely in-process, and reports sustained grain-call throughput.

**Architecture:** Each grid cell is a `ChunkGrain` holding its bodies as an in-grain `List<Body>`. Each simulation tick, a chunk computes local pairwise gravity, pulls a cheap `(centerOfMass, totalMass)` aggregate from each of up to 26 neighbor chunks via a real grain call, integrates, and hands off any body that crossed a chunk boundary. A driver (`AstroSimRunner`) seeds bodies, runs ticks for a configured duration against a single in-process `TestCluster` silo, and reports messages/sec from a custom `IQuarkDiagnosticListener` that counts every `OnInvocationEnd`.

**Tech Stack:** .NET 10, Quark.Runtime (`LocalGrainCallInvoker`), Quark.Testing `TestCluster`, hand-written grain proxy/invokables (no code generator, per house convention for test/benchmark projects), `System.Numerics.Vector3`.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md` — every task below implements one section of it.
- Lives entirely in `tests/Quark.Performance/AstroSim/` (new subfolder) + one edit to `tests/Quark.Performance/Program.cs` + one edit to `tests/Quark.Performance/Quark.Performance.csproj`. No changes outside this project.
- Single in-process silo: `TestClusterOptions.InitialSilosCount = 1` (the harness default is 2 — must be set explicitly).
- No `[GenerateSerializer]` on `Body`/`ChunkAggregate` — all calls are in-process and never serialize. `Serialize`/`DeserializeResult` on the hand-written invokables exist only to satisfy the interface contract and are never invoked by the local call path.
- No persistence, no TCP, no `samples/` entry, no new automated tests — this is a headless perf harness (matches `LocalStreamingTest`'s existing precedent of no test coverage). Validation is a manual run (Task 7).
- `dotnet build tests/Quark.Performance/Quark.Performance.csproj` must succeed after every task.

---

## File structure

```
tests/Quark.Performance/
  AstroSim/
    IChunkGrain.cs              — Task 1: grain contract, Body, ChunkAggregate, AstroSimOptions
    ChunkGrainBehavior.cs       — Task 2: ChunkState + the grain behavior (physics + handoff)
    ChunkGrainInvokables.cs     — Task 3: hand-written IGrainInvokable/IGrainVoidInvokable structs
    ChunkGrainProxy.cs          — Task 4: hand-written grain proxy
    BenchmarkDiagnosticListener.cs — Task 5: invocation counter for throughput measurement
    AstroSimRunner.cs           — Task 6: CLI args, DI wiring, seeding, tick loop, reporting
  Program.cs                    — Task 6: add "AstroSim" subcommand dispatch
  Quark.Performance.csproj      — Task 1: add 3 project references
```

---

### Task 1: Project references + grain contract types

**Files:**
- Modify: `tests/Quark.Performance/Quark.Performance.csproj`
- Create: `tests/Quark.Performance/AstroSim/IChunkGrain.cs`

**Interfaces:**
- Produces: `IChunkGrain` (methods `TickAsync()`, `GetAggregateAsync()`, `TransferBodyAsync(Body)`, `SeedAsync(IReadOnlyList<Body>)`), `Body` struct (`Position`, `Velocity`: `Vector3`; `Mass`: `float`), `ChunkAggregate` record struct (`CenterOfMass`: `Vector3`, `TotalMass`: `float`, `BodyCount`: `int`), `AstroSimOptions` class (`GridSize`: `int`, `CellSize`: `float`, `Dt`: `float`) — all in namespace `Quark.Performance.AstroSim`. Every later task consumes these exact names/types.

- [ ] **Step 1: Add project references**

In `tests/Quark.Performance/Quark.Performance.csproj`, add three lines to the existing `<ItemGroup>` of `ProjectReference`s (after the `Quark.Serialization.Abstractions` line):

```xml
    <ProjectReference Include="..\..\src\Quark.Persistence.Abstractions\Quark.Persistence.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\Quark.Diagnostics\Quark.Diagnostics.csproj" />
    <ProjectReference Include="..\..\src\Quark.Diagnostics.Abstractions\Quark.Diagnostics.Abstractions.csproj" />
```

- [ ] **Step 2: Create the grain contract file**

Create `tests/Quark.Performance/AstroSim/IChunkGrain.cs`:

```csharp
using System.Numerics;
using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.AstroSim;

public interface IChunkGrain : IGrainWithStringKey
{
    ValueTask TickAsync();
    ValueTask<ChunkAggregate> GetAggregateAsync();
    ValueTask TransferBodyAsync(Body body);
    ValueTask SeedAsync(IReadOnlyList<Body> bodies);
}

public struct Body
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Mass;
}

public readonly record struct ChunkAggregate(Vector3 CenterOfMass, float TotalMass, int BodyCount);

public sealed class AstroSimOptions
{
    public required int GridSize { get; init; }
    public required float CellSize { get; init; }
    public required float Dt { get; init; }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds (0 errors). The new types are unused so far — that's fine.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Performance/Quark.Performance.csproj tests/Quark.Performance/AstroSim/IChunkGrain.cs
git commit -m "astrosim: add grain contract types and project references"
```

---

### Task 2: Chunk grain behavior (physics + boundary handoff)

**Files:**
- Create: `tests/Quark.Performance/AstroSim/ChunkGrainBehavior.cs`

**Interfaces:**
- Consumes: `IChunkGrain`, `Body`, `ChunkAggregate`, `AstroSimOptions` (Task 1).
- Produces: `ChunkState` class (`Gate`: `object`, `Bodies`: `List<Body>`, `X`/`Y`/`Z`: `int`, `CoordsInitialized`: `bool`) and `ChunkGrainBehavior` class (`[Reentrant]`) — Task 6's DI wiring registers `IActivationMemory<ChunkState>` and constructs `ChunkGrainBehavior` by name.

- [ ] **Step 1: Create the behavior file**

**This exact class must be marked `[Reentrant]`.** Chunk A's `TickAsync` awaits a call into chunk B while
chunk B's own concurrently-running `TickAsync` awaits a call back into chunk A — without `[Reentrant]` this
is a guaranteed cross-grain deadlock (confirmed by hand: a 512-chunk run hung permanently on the very first
tick). `[Reentrant]` gets you no thread-safety for free (per `wiki/Lifecycle-and-Failure-Semantics.md`,
"calls interleave, and your state must tolerate that") — incoming `GetAggregateAsync`/`TransferBodyAsync`/
`SeedAsync` calls can run concurrently with this chunk's own in-flight `TickAsync`. `ChunkState` therefore
carries a plain lock object (`Gate`), and every method holds it only for brief synchronous sections, never
across an `await`. `TickAsync` snapshots `Bodies` into a private array before its neighbor-await loop,
computes entirely against that private copy, then commits the result back under a second short lock.

Create `tests/Quark.Performance/AstroSim/ChunkGrainBehavior.cs`:

```csharp
using System.Numerics;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Performance.AstroSim;

public sealed class ChunkState
{
    public readonly object Gate = new();
    public readonly List<Body> Bodies = new();
    public int X;
    public int Y;
    public int Z;
    public bool CoordsInitialized;
}

[Reentrant]
public sealed class ChunkGrainBehavior : IGrainBehavior, IChunkGrain, IActivationLifecycle
{
    private const float G = 1f;
    private const float Softening = 0.5f;

    private readonly IActivationMemory<ChunkState> _memory;
    private readonly IGrainFactory _factory;
    private readonly ICallContext _ctx;
    private readonly AstroSimOptions _options;

    public ChunkGrainBehavior(
        IActivationMemory<ChunkState> memory,
        IGrainFactory factory,
        ICallContext ctx,
        AstroSimOptions options)
    {
        _memory = memory;
        _factory = factory;
        _ctx = ctx;
        _options = options;
    }

    private ChunkState S => _memory.Value;

    public Task OnActivateAsync(CancellationToken ct)
    {
        if (!S.CoordsInitialized)
        {
            string[] parts = _ctx.GrainId.Key.Split(',');
            S.X = int.Parse(parts[0]);
            S.Y = int.Parse(parts[1]);
            S.Z = int.Parse(parts[2]);
            S.CoordsInitialized = true;
        }

        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

    public ValueTask SeedAsync(IReadOnlyList<Body> bodies)
    {
        lock (S.Gate)
        {
            S.Bodies.AddRange(bodies);
        }

        return default;
    }

    public ValueTask<ChunkAggregate> GetAggregateAsync()
    {
        Vector3 weightedSum = Vector3.Zero;
        float totalMass = 0f;
        int count;

        lock (S.Gate)
        {
            List<Body> bodies = S.Bodies;
            count = bodies.Count;
            foreach (Body b in bodies)
            {
                weightedSum += b.Position * b.Mass;
                totalMass += b.Mass;
            }
        }

        if (count == 0)
            return new ValueTask<ChunkAggregate>(new ChunkAggregate(Vector3.Zero, 0f, 0));

        return new ValueTask<ChunkAggregate>(new ChunkAggregate(weightedSum / totalMass, totalMass, count));
    }

    public ValueTask TransferBodyAsync(Body body)
    {
        lock (S.Gate)
        {
            S.Bodies.Add(body);
        }

        return default;
    }

    public async ValueTask TickAsync()
    {
        // This grain is [Reentrant]: the await below (neighbor.GetAggregateAsync) can interleave
        // with concurrent calls into THIS activation. Never mutate S.Bodies across an await —
        // snapshot to a private array first, do all the awaiting against that copy, and commit the
        // result back under a brief, non-awaiting lock.
        Body[] snapshot;
        lock (S.Gate)
        {
            snapshot = S.Bodies.Count == 0 ? Array.Empty<Body>() : S.Bodies.ToArray();
        }

        int n = snapshot.Length;
        if (n == 0)
            return;

        var forces = new Vector3[n];

        // Local pairwise gravity: O(k^2), k = bodies in this chunk.
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                Vector3 delta = snapshot[j].Position - snapshot[i].Position;
                float distSq = delta.LengthSquared() + Softening;
                float invDist3 = 1f / (MathF.Sqrt(distSq) * distSq);
                Vector3 unit = delta * invDist3;
                forces[i] += unit * (G * snapshot[j].Mass);
                forces[j] -= unit * (G * snapshot[i].Mass);
            }
        }

        // Neighbor aggregate pulls — the dominant message source (up to 26 grain calls/tick).
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                        continue;

                    int nx = S.X + dx, ny = S.Y + dy, nz = S.Z + dz;
                    if (nx < 0 || nx >= _options.GridSize ||
                        ny < 0 || ny >= _options.GridSize ||
                        nz < 0 || nz >= _options.GridSize)
                        continue;

                    IChunkGrain neighbor = _factory.GetGrain<IChunkGrain>($"{nx},{ny},{nz}");
                    ChunkAggregate agg = await neighbor.GetAggregateAsync();
                    if (agg.BodyCount == 0)
                        continue;

                    for (int i = 0; i < n; i++)
                    {
                        Vector3 delta = agg.CenterOfMass - snapshot[i].Position;
                        float distSq = delta.LengthSquared() + Softening;
                        float invDist3 = 1f / (MathF.Sqrt(distSq) * distSq);
                        forces[i] += delta * invDist3 * (G * agg.TotalMass);
                    }
                }
            }
        }

        // Integrate and clamp against the private snapshot — no lock needed here, nothing else can
        // see it.
        float worldMax = _options.GridSize * _options.CellSize;
        var retained = new List<Body>(n);
        List<(int x, int y, int z, Body body)>? transfers = null;

        for (int i = 0; i < n; i++)
        {
            Body b = snapshot[i];
            b.Velocity += forces[i] / MathF.Max(b.Mass, 0.0001f) * _options.Dt;
            b.Position += b.Velocity * _options.Dt;

            ClampAxis(ref b.Position.X, ref b.Velocity.X, worldMax);
            ClampAxis(ref b.Position.Y, ref b.Velocity.Y, worldMax);
            ClampAxis(ref b.Position.Z, ref b.Velocity.Z, worldMax);

            int destX = Math.Clamp((int)(b.Position.X / _options.CellSize), 0, _options.GridSize - 1);
            int destY = Math.Clamp((int)(b.Position.Y / _options.CellSize), 0, _options.GridSize - 1);
            int destZ = Math.Clamp((int)(b.Position.Z / _options.CellSize), 0, _options.GridSize - 1);

            if (destX != S.X || destY != S.Y || destZ != S.Z)
                (transfers ??= new List<(int, int, int, Body)>()).Add((destX, destY, destZ, b));
            else
                retained.Add(b);
        }

        // Commit: replace the n bodies we snapshotted with their integrated results. Bodies appended
        // concurrently by TransferBodyAsync/SeedAsync while we awaited neighbors are untouched — this
        // grain is reentrant, but only one TickAsync runs per chunk at a time (the driver awaits a
        // full tick before starting the next), and Transfer/Seed only ever append, so the first n
        // entries in the live list are still exactly the ones we snapshotted.
        lock (S.Gate)
        {
            S.Bodies.RemoveRange(0, n);
            S.Bodies.InsertRange(0, retained);
        }

        if (transfers is null)
            return;

        var transferTasks = new Task[transfers.Count];
        for (int i = 0; i < transfers.Count; i++)
        {
            (int x, int y, int z, Body body) = transfers[i];
            IChunkGrain dest = _factory.GetGrain<IChunkGrain>($"{x},{y},{z}");
            transferTasks[i] = dest.TransferBodyAsync(body).AsTask();
        }

        await Task.WhenAll(transferTasks);
    }

    private static void ClampAxis(ref float pos, ref float vel, float max)
    {
        if (pos < 0f)
        {
            pos = 0f;
            vel = -vel;
        }
        else if (pos >= max)
        {
            pos = max - 0.001f;
            vel = -vel;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds. `ChunkGrainBehavior` compiles standalone — nothing constructs it yet.

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Performance/AstroSim/ChunkGrainBehavior.cs
git commit -m "astrosim: add chunk grain behavior with local + neighbor gravity"
```

---

### Task 3: Hand-written invokables

**Files:**
- Create: `tests/Quark.Performance/AstroSim/ChunkGrainInvokables.cs`

**Interfaces:**
- Consumes: `IChunkGrain`, `Body`, `ChunkAggregate` (Task 1).
- Produces: `ChunkBehavior_TickInvokable` (`MethodId` 0), `ChunkBehavior_GetAggregateInvokable` (`MethodId` 1), `ChunkBehavior_TransferBodyInvokable` (`MethodId` 2), `ChunkBehavior_SeedInvokable` (`MethodId` 3) — Task 4's proxy constructs and passes these to `IGrainCallInvoker`.

- [ ] **Step 1: Create the invokables file**

Create `tests/Quark.Performance/AstroSim/ChunkGrainInvokables.cs`:

```csharp
using System.Numerics;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance.AstroSim;

internal readonly struct ChunkBehavior_TickInvokable : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IChunkGrain)behavior).TickAsync();
    public void Serialize(ref CodecWriter writer) { }
}

internal readonly struct ChunkBehavior_GetAggregateInvokable : IGrainInvokable<ChunkAggregate>
{
    public uint MethodId => 1u;
    public ValueTask<ChunkAggregate> Invoke(IGrainBehavior behavior) => ((IChunkGrain)behavior).GetAggregateAsync();
    public void Serialize(ref CodecWriter writer) { }

    public ChunkAggregate DeserializeResult(ref CodecReader reader)
    {
        float x = BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
        float y = BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
        float z = BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
        float totalMass = BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
        int bodyCount = reader.ReadInt32();
        return new ChunkAggregate(new Vector3(x, y, z), totalMass, bodyCount);
    }
}

internal readonly struct ChunkBehavior_TransferBodyInvokable(Body body) : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IChunkGrain)behavior).TransferBodyAsync(body);

    public void Serialize(ref CodecWriter writer)
    {
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.X));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.Y));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.Z));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.X));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.Y));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.Z));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Mass));
    }
}

internal readonly struct ChunkBehavior_SeedInvokable(IReadOnlyList<Body> bodies) : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IChunkGrain)behavior).SeedAsync(bodies);

    public void Serialize(ref CodecWriter writer)
    {
        writer.WriteInt32(bodies.Count);
        foreach (Body body in bodies)
        {
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.X));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.Y));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.Z));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.X));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.Y));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.Z));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Mass));
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Performance/AstroSim/ChunkGrainInvokables.cs
git commit -m "astrosim: add hand-written grain invokables"
```

---

### Task 4: Grain proxy

**Files:**
- Create: `tests/Quark.Performance/AstroSim/ChunkGrainProxy.cs`

**Interfaces:**
- Consumes: `IChunkGrain` (Task 1), the four invokable structs (Task 3), `IGrainProxyActivator<TSelf>` / `IGrainCallInvoker` (`Quark.Core.Abstractions.Hosting`, existing).
- Produces: `ChunkGrainProxy` class — Task 6 registers it via `services.AddGrainProxy<IChunkGrain, ChunkGrainProxy>()`.

- [ ] **Step 1: Create the proxy file**

Create `tests/Quark.Performance/AstroSim/ChunkGrainProxy.cs`:

```csharp
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Performance.AstroSim;

public sealed class ChunkGrainProxy : IChunkGrain, IGrainProxyActivator<ChunkGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public ChunkGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static ChunkGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public ValueTask TickAsync()
        => _invoker.InvokeVoidAsync(_grainId, new ChunkBehavior_TickInvokable());

    public ValueTask<ChunkAggregate> GetAggregateAsync()
        => _invoker.InvokeAsync<ChunkBehavior_GetAggregateInvokable, ChunkAggregate>(
            _grainId, new ChunkBehavior_GetAggregateInvokable());

    public ValueTask TransferBodyAsync(Body body)
        => _invoker.InvokeVoidAsync(_grainId, new ChunkBehavior_TransferBodyInvokable(body));

    public ValueTask SeedAsync(IReadOnlyList<Body> bodies)
        => _invoker.InvokeVoidAsync(_grainId, new ChunkBehavior_SeedInvokable(bodies));
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Performance/AstroSim/ChunkGrainProxy.cs
git commit -m "astrosim: add hand-written chunk grain proxy"
```

---

### Task 5: Throughput-counting diagnostic listener

**Files:**
- Create: `tests/Quark.Performance/AstroSim/BenchmarkDiagnosticListener.cs`

**Interfaces:**
- Consumes: `IQuarkDiagnosticListener`, `InvocationEndEvent` (`Quark.Diagnostics.Abstractions`, existing).
- Produces: `BenchmarkDiagnosticListener` class with a public `long Count` property — Task 6 constructs one instance, registers it via `services.AddSingleton<IQuarkDiagnosticListener>(listener)` (**not** `services.AddQuarkDiagnostics(listener)` — see Task 6 Step 1 for why that helper must not be used here), and reads `listener.Count` directly from the driver loop (not via DI resolution).

- [ ] **Step 1: Create the listener file**

Create `tests/Quark.Performance/AstroSim/BenchmarkDiagnosticListener.cs`:

```csharp
using Quark.Diagnostics.Abstractions;

namespace Quark.Performance.AstroSim;

public sealed class BenchmarkDiagnosticListener : IQuarkDiagnosticListener
{
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public void OnInvocationEnd(in InvocationEndEvent e) => Interlocked.Increment(ref _count);
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tests/Quark.Performance/Quark.Performance.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Performance/AstroSim/BenchmarkDiagnosticListener.cs
git commit -m "astrosim: add invocation-counting diagnostic listener"
```

---

### Task 6: Driver — DI wiring, seeding, tick loop, reporting

**Files:**
- Create: `tests/Quark.Performance/AstroSim/AstroSimRunner.cs`
- Modify: `tests/Quark.Performance/Program.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–5 (`IChunkGrain`, `Body`, `AstroSimOptions`, `ChunkState`, `ChunkGrainBehavior`, `ChunkGrainProxy`, `BenchmarkDiagnosticListener`), plus existing runtime types `TestCluster` (`Quark.Testing.Harness`), `AddQuarkRuntime`/`AddGrainBehavior` (`Quark.Runtime`), `AddLocalClusterClient`/`AddGrainProxy` (`Quark.Client`), `IQuarkDiagnosticListener` (`Quark.Diagnostics.Abstractions`), `ActivationMemoryAccessor<T>` (`Quark.Persistence.Abstractions`), `IActivationShellAccessor` (`Quark.Runtime`).
- Produces: `AstroSimRunner.RunAsync(string[] args)` — called from `Program.cs`.

- [ ] **Step 1: Create the runner file**

Create `tests/Quark.Performance/AstroSim/AstroSimRunner.cs`:

```csharp
using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Diagnostics.Abstractions;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.AstroSim;

public static class AstroSimRunner
{
    public static async Task RunAsync(string[] args)
    {
        AstroSimCliArgs cli = AstroSimCliArgs.Parse(args);
        var simOptions = new AstroSimOptions { GridSize = cli.Grid, CellSize = 100f, Dt = 0.01f };
        var listener = new BenchmarkDiagnosticListener();
        int totalChunks = cli.Grid * cli.Grid * cli.Grid;

        Console.WriteLine("=== AstroSim Throughput Benchmark ===");
        Console.WriteLine($"  Bodies: {cli.Bodies:N0}, Grid: {cli.Grid}^3 ({totalChunks:N0} chunks), Duration: {cli.DurationSeconds}s");
        Console.WriteLine();

        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                // NOT services.AddQuarkDiagnostics(listener) — that helper
                // (Quark.Diagnostics/DiagnosticsServiceCollectionExtensions.cs, itself marked
                // "did not implemented or used in any elsewhere") is circular: its EnsureComposite
                // step registers IQuarkDiagnosticListener as a factory that resolves
                // CompositeDiagnosticListener, whose constructor resolves
                // IEnumerable<IQuarkDiagnosticListener> — which includes that very factory.
                // Resolving IQuarkDiagnosticListener (which happens on every grain call) then
                // self-recurses and the silo never finishes starting (confirmed: hangs forever,
                // verified with dotnet-dump). Registering the listener instance directly avoids
                // the composite machinery entirely.
                services.AddSingleton<IQuarkDiagnosticListener>(listener);
                services.AddSingleton(simOptions);
                services.AddGrainBehavior<IChunkGrain, ChunkGrainBehavior>();
                services.AddScoped<IActivationMemory<ChunkState>>(sp =>
                    new ActivationMemoryAccessor<ChunkState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<ChunkState>()));
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IChunkGrain, ChunkGrainProxy>();
            };
        });

        int grid = cli.Grid;
        var chunkGrains = new IChunkGrain[totalChunks];
        for (int x = 0; x < grid; x++)
        {
            for (int y = 0; y < grid; y++)
            {
                for (int z = 0; z < grid; z++)
                {
                    chunkGrains[ChunkIndex(x, y, z, grid)] = cluster.Client.GetGrain<IChunkGrain>($"{x},{y},{z}");
                }
            }
        }

        Console.WriteLine("Seeding bodies...");
        var random = new Random(42);
        float worldSize = grid * simOptions.CellSize;
        var perChunkBodies = new List<Body>?[totalChunks];

        for (int i = 0; i < cli.Bodies; i++)
        {
            var position = new Vector3(
                (float)(random.NextDouble() * worldSize),
                (float)(random.NextDouble() * worldSize),
                (float)(random.NextDouble() * worldSize));

            int cx = Math.Clamp((int)(position.X / simOptions.CellSize), 0, grid - 1);
            int cy = Math.Clamp((int)(position.Y / simOptions.CellSize), 0, grid - 1);
            int cz = Math.Clamp((int)(position.Z / simOptions.CellSize), 0, grid - 1);
            int idx = ChunkIndex(cx, cy, cz, grid);

            var body = new Body { Position = position, Mass = 1f + (float)random.NextDouble() };
            (perChunkBodies[idx] ??= new List<Body>()).Add(body);
        }

        for (int i = 0; i < totalChunks; i++)
        {
            if (perChunkBodies[i] is { Count: > 0 } bodies)
                await chunkGrains[i].SeedAsync(bodies);
        }

        Console.WriteLine("Seeding complete. Running simulation...\n");

        long startCount = listener.Count;
        var totalSw = Stopwatch.StartNew();
        var reportSw = Stopwatch.StartNew();
        var duration = TimeSpan.FromSeconds(cli.DurationSeconds);
        int ticks = 0;

        while (totalSw.Elapsed < duration)
        {
            await Task.WhenAll(chunkGrains.Select(static g => g.TickAsync().AsTask()));
            ticks++;

            if (reportSw.Elapsed.TotalSeconds >= 1)
            {
                long delta = listener.Count - startCount;
                Console.WriteLine($"  t={totalSw.Elapsed.TotalSeconds:F0}s  {delta / totalSw.Elapsed.TotalSeconds:N0} msg/s (cumulative avg), ticks={ticks}");
                reportSw.Restart();
            }
        }

        totalSw.Stop();
        long totalMessages = listener.Count - startCount;

        ChunkAggregate[] finalAggregates = await Task.WhenAll(chunkGrains.Select(static g => g.GetAggregateAsync().AsTask()));
        ChunkAggregate[] populated = finalAggregates.Where(static a => a.BodyCount > 0).ToArray();
        float minCoord = populated.Length == 0 ? 0f : populated.Min(static a => Math.Min(a.CenterOfMass.X, Math.Min(a.CenterOfMass.Y, a.CenterOfMass.Z)));
        float maxCoord = populated.Length == 0 ? 0f : populated.Max(static a => Math.Max(a.CenterOfMass.X, Math.Max(a.CenterOfMass.Y, a.CenterOfMass.Z)));

        Console.WriteLine();
        Console.WriteLine("=== AstroSim Complete ===");
        Console.WriteLine($"  Bodies simulated: {cli.Bodies:N0}");
        Console.WriteLine($"  Grid: {grid}x{grid}x{grid} ({totalChunks:N0} chunks)");
        Console.WriteLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s, Ticks: {ticks}");
        Console.WriteLine($"  Total messages: {totalMessages:N0}");
        Console.WriteLine($"  Sustained throughput: {totalMessages / totalSw.Elapsed.TotalSeconds:N0} msg/s");
        Console.WriteLine($"  Final chunk center-of-mass bounds: [{minCoord:F1}, {maxCoord:F1}] (world is [0, {worldSize:F0}])");
    }

    private static int ChunkIndex(int x, int y, int z, int grid) => (x * grid + y) * grid + z;
}

internal sealed class AstroSimCliArgs
{
    public int Bodies { get; private init; } = 10_000_000;
    public int Grid { get; private init; } = 32;
    public double DurationSeconds { get; private init; } = 10;

    public static AstroSimCliArgs Parse(string[] args)
    {
        int bodies = 10_000_000;
        int grid = 32;
        double duration = 10;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bodies" when i + 1 < args.Length:
                    bodies = int.Parse(args[++i]);
                    break;
                case "--grid" when i + 1 < args.Length:
                    grid = int.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
            }
        }

        return new AstroSimCliArgs { Bodies = bodies, Grid = grid, DurationSeconds = duration };
    }
}
```

- [ ] **Step 2: Wire the subcommand into Program.cs**

In `tests/Quark.Performance/Program.cs`, add a `using Quark.Performance.AstroSim;` and a second subcommand check right after the existing `LocalStreaming` block:

```csharp
using BenchmarkDotNet.Running;
using Quark.Performance.AstroSim;

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
git add tests/Quark.Performance/AstroSim/AstroSimRunner.cs tests/Quark.Performance/Program.cs
git commit -m "astrosim: add CLI driver, DI wiring, and Program.cs subcommand"
```

---

### Task 7: Manual validation run

**Files:** none (execution only).

**Already done once, by hand, while writing this plan** — Tasks 1–6's exact code was built and run
end-to-end against a live silo at 100K bodies/8³ grid and 1M bodies/16³ grid: no exceptions, bounded
output, sane center-of-mass figures. Re-run it here to confirm on your own checkout; you should not hit
either of the two bugs already fixed in Task 2/Task 6 (the reentrancy deadlock and the `AddQuarkDiagnostics`
circular-DI hang) — if you do, something in Tasks 1–6 was copied incorrectly.

- [ ] **Step 1: Small-scale smoke run**

Run: `dotnet run --project tests/Quark.Performance -- AstroSim --bodies 100000 --grid 8 --duration 5`

Expected: No exceptions or stack traces. Console prints the seeding message, one or more rolling `msg/s` lines, and a final `=== AstroSim Complete ===` block with a nonzero `Sustained throughput`. The `Final chunk center-of-mass bounds` line must fall within `[0, 800]` (world size for `--grid 8` at `CellSize=100`) — if it doesn't, `ClampAxis` in `ChunkGrainBehavior.TickAsync` has a bug and must be fixed before continuing.

- [ ] **Step 2: Tune stability if needed**

If bodies appear to blow up (bounds wildly outside `[0, worldSize]` before clamping kicks in, or the run throws an `OverflowException`/produces `NaN` — visible as `Sustained throughput` still printing but center-of-mass bounds reading `NaN`), reduce `G` in `ChunkGrainBehavior.cs` (e.g. `0.1f`) or increase `Softening` (e.g. `2f`) and re-run Step 1. If bodies barely move (bounds stay within a tiny fraction of `worldSize` of their seeded spread — expected since initial velocity is zero and forces are small), that is acceptable: the goal is a stable, non-diverging simulation generating real grain traffic, not visual realism.

- [ ] **Step 3: Full-scale target run**

Run: `dotnet run -c Release --project tests/Quark.Performance -- AstroSim`

(Uses the default `--bodies 10000000 --grid 32 --duration 10`.) **This is slow, not stuck**: at ~305
bodies/chunk the O(k²) local-gravity step plus the 26 sequential, awaited neighbor round-trips per chunk
dominate wall-clock time — a smaller check (1M bodies, 16³ grid, ~244 bodies/chunk) took ~14s for a single
tick, so the full 10M/32³ run will overshoot the requested 10s `--duration` substantially (the loop only
checks elapsed time *between* ticks, not mid-tick) and may take several minutes for even a handful of ticks.
Let it run to completion — don't kill it prematurely assuming a hang. Record the final `Sustained
throughput` figure; expect it far below the ~90M msg/s ceiling (that gap is the interesting result, not a
sign something's broken — see the design spec §7).

- [ ] **Step 4: Report the result**

Report the full-scale run's `Sustained throughput` (msg/sec) next to the ~90M msg/s microbenchmark ceiling, in the PR description or commit message for this task — no code changes, so nothing to commit for this step.
