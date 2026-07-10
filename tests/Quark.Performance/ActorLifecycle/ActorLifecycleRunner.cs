using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Performance.Shared;
using Quark.Runtime;
using Quark.Serialization;

namespace Quark.Performance.ActorLifecycle;

/// <summary>
///     Measures actor creation/destruction cost. Bypasses <c>TestCluster</c>/proxies entirely (like
///     <c>DispatchPipelineBenchmarks.SetupAsync</c>) so a fresh, unique <see cref="GrainId"/> can be
///     forced through <see cref="GrainActivationTable.GetOrCreateAsync"/> every iteration -- a
///     genuinely new activation, not a warm one. Destruction goes through
///     <see cref="GrainActivationTable.TryDeactivateAsync"/>, previously unused (dead) code that
///     awaits a real <see cref="GrainActivation.DisposeAsync"/>.
/// </summary>
public static class ActorLifecycleRunner
{
    private static readonly GrainType ActorLifecycleGrainType = new("ActorLifecycleGrain");

    public static async Task RunAsync(string[] args)
    {
        ActorLifecycleCliArgs cli = ActorLifecycleCliArgs.Parse(args);
        int parallelism = cli.Parallelism;
        if (cli.Allocations && parallelism != 1)
        {
            Console.WriteLine($"  --allocations requires clean single-threaded GC attribution -- forcing --parallelism 1 (requested: {cli.Parallelism}).");
            parallelism = 1;
        }

        Console.WriteLine("=== Actor Lifecycle Benchmark ===");
        Console.WriteLine($"  Parallelism: {parallelism}, Duration: {cli.DurationSeconds}s, Allocations: {cli.Allocations}");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkSerialization();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "actor-lifecycle-bench";
            o.ServiceId = "actor-lifecycle-bench";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddGrainBehavior<IActorLifecycleGrain, ActorLifecycleGrainBehavior>();
        await using ServiceProvider sp = services.BuildServiceProvider();

        sp.GetRequiredService<GrainTypeRegistry>().Register(ActorLifecycleGrainType, typeof(ActorLifecycleGrainBehavior));

        GrainActivationTable activationTable = sp.GetRequiredService<GrainActivationTable>();
        IGrainCallInvoker invoker = sp.GetRequiredService<IGrainCallInvoker>();

        using var createHistogram = new LatencyHistogram();
        using var destroyHistogram = new LatencyHistogram();
        using LatencyHistogram? allocationHistogram = cli.Allocations ? new LatencyHistogram() : null;

        var counters = new PaddedCounter[parallelism];
        long globalSeq = 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));
        var totalSw = Stopwatch.StartNew();

        async Task RunWorkerAsync(int workerIndex)
        {
            long local = 0;
            while (!cts.IsCancellationRequested)
            {
                long seq = Interlocked.Increment(ref globalSeq);
                GrainId grainId = GrainId.Create(ActorLifecycleGrainType, $"actor-{workerIndex}-{seq}");

                long allocBefore = cli.Allocations ? GC.GetTotalAllocatedBytes(precise: true) : 0;
                long createStart = Stopwatch.GetTimestamp();
                try
                {
                    await invoker.InvokeVoidAsync(grainId, new ActorLifecycleBehavior_PingInvokable());
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                createHistogram.Record(Stopwatch.GetElapsedTime(createStart).TotalMicroseconds);

                long destroyStart = Stopwatch.GetTimestamp();
                await activationTable.TryDeactivateAsync(grainId);
                destroyHistogram.Record(Stopwatch.GetElapsedTime(destroyStart).TotalMicroseconds);

                if (cli.Allocations)
                {
                    long allocAfter = GC.GetTotalAllocatedBytes(precise: true);
                    allocationHistogram!.Record(allocAfter - allocBefore);
                }

                local++;
                Volatile.Write(ref counters[workerIndex].Value, local);
            }
        }

        var workerTasks = new Task[parallelism];
        for (int w = 0; w < parallelism; w++)
        {
            int workerIndex = w;
            workerTasks[w] = Task.Run(() => RunWorkerAsync(workerIndex));
        }

        await Task.WhenAll(workerTasks);
        totalSw.Stop();

        long totalRoundTrips = SumCounters(counters);
        double totalSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine("=== Actor Lifecycle Complete ===");
        Console.WriteLine($"  Duration: {totalSeconds:F1}s");
        Console.WriteLine($"  Creations:    {totalRoundTrips:N0} ({totalRoundTrips / totalSeconds:N0}/s)");
        Console.WriteLine($"  Destructions: {totalRoundTrips:N0} ({totalRoundTrips / totalSeconds:N0}/s)");
        Console.WriteLine($"  Create latency:  {createHistogram.Merge()}");
        Console.WriteLine($"  Destroy latency: {destroyHistogram.Merge()}");

        if (cli.Allocations)
        {
            Percentiles alloc = allocationHistogram!.Merge();
            Console.WriteLine($"  Allocation/op: n={alloc.Count:N0} mean={alloc.Mean:N0}B p50={alloc.P50:N0}B p90={alloc.P90:N0}B p99={alloc.P99:N0}B max={alloc.Max:N0}B");
            Console.WriteLine("  Note: process-wide GC.GetTotalAllocatedBytes(precise:true) delta -- background");
            Console.WriteLine("  scheduler-worker threads and GC bookkeeping still add some noise; read the");
            Console.WriteLine("  median across many iterations, not a single sample.");
        }
    }

    private static long SumCounters(PaddedCounter[] counters)
    {
        long total = 0;
        for (int i = 0; i < counters.Length; i++)
        {
            total += Volatile.Read(ref counters[i].Value);
        }

        return total;
    }
}

internal sealed class ActorLifecycleCliArgs
{
    public int Parallelism { get; private init; } = Environment.ProcessorCount;
    public double DurationSeconds { get; private init; } = 10;
    public bool Allocations { get; private init; }

    public static ActorLifecycleCliArgs Parse(string[] args)
    {
        int parallelism = Environment.ProcessorCount;
        double duration = 10;
        bool allocations = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--parallelism" when i + 1 < args.Length:
                    parallelism = int.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
                case "--allocations":
                    allocations = true;
                    break;
            }
        }

        return new ActorLifecycleCliArgs
        {
            Parallelism = parallelism,
            DurationSeconds = duration,
            Allocations = allocations,
        };
    }
}
