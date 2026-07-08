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
            // Task.WhenAll(chunkGrains.Select(g => g.TickAsync().AsTask())) would NOT actually
            // parallelize this: Select is lazily enumerated by WhenAll, and since [Reentrant]
            // in-process grain calls complete synchronously (no real I/O, no thread hop), each
            // TickAsync() runs to full completion on the calling thread before the next one even
            // starts — the whole tick collapses onto a single core. Task.Run forces each chunk's
            // tick onto the thread pool so they genuinely run concurrently.
            await Task.WhenAll(chunkGrains.Select(static g => Task.Run(() => g.TickAsync().AsTask())));
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
