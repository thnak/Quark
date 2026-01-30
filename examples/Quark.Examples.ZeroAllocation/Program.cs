using System.Diagnostics;
using Quark.Core.Actors;
using Quark.Core.Actors.Pooling;

namespace Quark.Examples.ZeroAllocation;

/// <summary>
///     Demonstrates the performance benefits of zero-allocation messaging with object pooling.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Quark Zero-Allocation Messaging Benchmark ===\n");

        const int iterations = 100_000;
        const int warmupIterations = 1_000;

        // Warmup
        Console.WriteLine("Warming up...");
        await RunBenchmark(warmupIterations, usePooling: true);
        await RunBenchmark(warmupIterations, usePooling: false);

        // Benchmark without pooling
        Console.WriteLine($"\nBenchmarking {iterations:N0} message allocations WITHOUT pooling...");
        var (timeWithoutPooling, memoryWithoutPooling) = await RunBenchmark(iterations, usePooling: false);
        Console.WriteLine($"  Time: {timeWithoutPooling.TotalMilliseconds:N2} ms");
        Console.WriteLine($"  Memory: {memoryWithoutPooling / 1024.0:N2} KB");
        Console.WriteLine($"  Throughput: {iterations / timeWithoutPooling.TotalSeconds:N0} msgs/sec");

        // Force GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Task.Delay(100);

        // Benchmark with pooling
        Console.WriteLine($"\nBenchmarking {iterations:N0} message allocations WITH pooling...");
        var (timeWithPooling, memoryWithPooling) = await RunBenchmark(iterations, usePooling: true);
        Console.WriteLine($"  Time: {timeWithPooling.TotalMilliseconds:N2} ms");
        Console.WriteLine($"  Memory: {memoryWithPooling / 1024.0:N2} KB");
        Console.WriteLine($"  Throughput: {iterations / timeWithPooling.TotalSeconds:N0} msgs/sec");

        // Calculate improvements
        var timeImprovement = (timeWithoutPooling.TotalMilliseconds - timeWithPooling.TotalMilliseconds) / timeWithoutPooling.TotalMilliseconds * 100;
        var memoryImprovement = (memoryWithoutPooling - memoryWithPooling) / (double)memoryWithoutPooling * 100;

        Console.WriteLine("\n=== Performance Improvements ===");
        Console.WriteLine($"  Time saved: {timeImprovement:N1}%");
        Console.WriteLine($"  Memory saved: {memoryImprovement:N1}%");
        Console.WriteLine($"  Speedup: {timeWithoutPooling.TotalMilliseconds / timeWithPooling.TotalMilliseconds:N2}x");

        Console.WriteLine("\n=== Message ID Generation Comparison ===");
        BenchmarkMessageIdGeneration();

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task<(TimeSpan time, long memory)> RunBenchmark(int iterations, bool usePooling)
    {
        var sw = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: false);

        if (usePooling)
        {
            for (int i = 0; i < iterations; i++)
            {
                using var message = ActorMessageFactory.CreatePooled<string>("TestMethod", "arg1", i);
                message.CompletionSource.SetResult($"result-{i}");
                await message.CompletionSource.Task;
            }
        }
        else
        {
            for (int i = 0; i < iterations; i++)
            {
                var message = ActorMessageFactory.Create<string>("TestMethod", "arg1", i);
                message.CompletionSource.SetResult($"result-{i}");
                await message.CompletionSource.Task;
            }
        }

        sw.Stop();
        var memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
        var memoryUsed = Math.Max(0, memoryAfter - memoryBefore);

        return (sw.Elapsed, memoryUsed);
    }

    private static void BenchmarkMessageIdGeneration()
    {
        const int iterations = 1_000_000;

        // Benchmark GUID generation (old way)
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var id = Guid.NewGuid().ToString();
        }
        sw.Stop();
        var guidTime = sw.Elapsed;

        // Benchmark incremental ID generation (new way)
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var id = MessageIdGenerator.Generate();
        }
        sw.Stop();
        var incrementalTime = sw.Elapsed;

        Console.WriteLine($"  GUID generation: {guidTime.TotalMilliseconds:N2} ms for {iterations:N0} IDs");
        Console.WriteLine($"  Incremental generation: {incrementalTime.TotalMilliseconds:N2} ms for {iterations:N0} IDs");
        Console.WriteLine($"  Speedup: {guidTime.TotalMilliseconds / incrementalTime.TotalMilliseconds:N2}x faster");
    }
}
