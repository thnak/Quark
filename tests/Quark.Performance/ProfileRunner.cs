using System.Diagnostics;

namespace Quark.Performance;

/// <summary>
/// Tight-loop drivers for dotnet-trace. BenchmarkDotNet's harness either forks a short-lived
/// out-of-process child (nothing to attach to ahead of time) or, with --inProcess, runs too briefly
/// per iteration for useful CPU sampling. These reuse each benchmark class's own
/// Setup/operation/Cleanup directly, in-process, in a fixed-duration loop, so
/// `dotnet-trace collect -- dotnet Quark.Performance.dll Profile &lt;target&gt;` captures a clean,
/// long-running sample of just that one hot path.
/// </summary>
public static class ProfileRunner
{
    public static async Task RunAsync(string[] args)
    {
        string target = args.Length > 1 ? args[1] : "UserServiceProviderFactory";
        double seconds = args.Length > 2 && double.TryParse(args[2], out double s) ? s : 5.0;
        var duration = TimeSpan.FromSeconds(seconds);

        switch (target)
        {
            case "UserServiceProviderFactory":
                RunUserServiceProviderFactory(duration);
                break;
            case "StreamFanOut":
                int subscriberCount = args.Length > 3 && int.TryParse(args[3], out int n) ? n : 1000;
                await RunStreamFanOutAsync(duration, subscriberCount);
                break;
            case "ActivationLifecycle":
                await RunActivationLifecycleAsync(duration);
                break;
            default:
                Console.WriteLine(
                    $"Unknown profile target '{target}'. Use UserServiceProviderFactory, StreamFanOut, or ActivationLifecycle.");
                break;
        }
    }

    private static void RunUserServiceProviderFactory(TimeSpan duration)
    {
        var bench = new UserServiceProviderFactoryBenchmarks();
        bench.Setup();

        Console.WriteLine("Warming up NotOptedIn_CreateScopeAndResolve...");
        for (int i = 0; i < 10_000; i++)
        {
            bench.NotOptedIn_CreateScopeAndResolve();
        }

        Console.WriteLine($"Running NotOptedIn_CreateScopeAndResolve for {duration}...");
        var sw = Stopwatch.StartNew();
        long iterations = 0;
        while (sw.Elapsed < duration)
        {
            for (int i = 0; i < 1000; i++)
            {
                bench.NotOptedIn_CreateScopeAndResolve();
            }

            iterations += 1000;
        }

        sw.Stop();
        Console.WriteLine(
            $"NotOptedIn_CreateScopeAndResolve: {iterations} iterations in {sw.Elapsed} " +
            $"({iterations / sw.Elapsed.TotalSeconds:N0} ops/sec)");

        bench.Cleanup();
    }

    private static async Task RunStreamFanOutAsync(TimeSpan duration, int subscriberCount)
    {
        var bench = new StreamFanOutBenchmarks { SubscriberCount = subscriberCount };
        await bench.GlobalSetup();

        Console.WriteLine("Warming up PublishToFanOut_NoOpSubscribers...");
        for (int i = 0; i < 200; i++)
        {
            await bench.PublishToFanOut_NoOpSubscribers();
        }

        Console.WriteLine(
            $"Running PublishToFanOut_NoOpSubscribers (SubscriberCount={subscriberCount}) for {duration}...");
        var sw = Stopwatch.StartNew();
        long iterations = 0;
        while (sw.Elapsed < duration)
        {
            await bench.PublishToFanOut_NoOpSubscribers();
            iterations++;
        }

        sw.Stop();
        Console.WriteLine(
            $"PublishToFanOut_NoOpSubscribers: {iterations} iterations in {sw.Elapsed} " +
            $"({iterations / sw.Elapsed.TotalSeconds:N0} ops/sec)");

        await bench.GlobalCleanup();
    }

    // ActivationLifecycleBenchmarks uses RunStrategy.ColdStart (exactly one real activation per
    // iteration, by design -- see its doc comment), which BenchmarkDotNet's InProcessEmitToolchain
    // rejects outright ("takes too long to run. Prefer to use out-of-process toolchains"). This
    // process can't use the out-of-process toolchain either (ambiguous Quark.Performance.csproj name
    // when a second worktree checkout exists alongside this one). Hand-timed fallback: not
    // BenchmarkDotNet-precision, but honest and reuses the exact same Setup/operation code.
    private static async Task RunActivationLifecycleAsync(TimeSpan duration)
    {
        var bench = new ActivationLifecycleBenchmarks();
        bench.Setup();

        TimeSpan per = duration / 3;

        Console.WriteLine("Warming up GrainActivate...");
        for (int i = 0; i < 200; i++)
        {
            await bench.GrainActivate();
        }

        await RunTimedAsync("GrainActivate", per, bench.GrainActivate);

        Console.WriteLine("Warming up GrainDeactivate (PrepareDeactivate untimed each iteration)...");
        for (int i = 0; i < 200; i++)
        {
            bench.PrepareDeactivate();
            await bench.GrainDeactivate();
        }

        Console.WriteLine($"Running GrainDeactivate for {per} (PrepareDeactivate excluded from timing)...");
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var wallClock = Stopwatch.StartNew();
        var opTimer = new Stopwatch();
        long iterations = 0;
        while (wallClock.Elapsed < per)
        {
            bench.PrepareDeactivate(); // untimed: activates a fresh grain for GrainDeactivate to tear down
            opTimer.Start();
            await bench.GrainDeactivate();
            opTimer.Stop();
            iterations++;
        }

        long allocBytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        Console.WriteLine(
            $"GrainDeactivate: {iterations} iterations in {opTimer.Elapsed} of GrainDeactivate-only time " +
            $"({opTimer.Elapsed.TotalMicroseconds / iterations:N2} us/op; " +
            $"{(double)allocBytes / iterations:N0} B/op total allocated, includes PrepareDeactivate)");

        Console.WriteLine("Warming up GrainActivateDeactivateRoundTrip...");
        for (int i = 0; i < 200; i++)
        {
            await bench.GrainActivateDeactivateRoundTrip();
        }

        await RunTimedAsync("GrainActivateDeactivateRoundTrip", per, bench.GrainActivateDeactivateRoundTrip);

        bench.Cleanup();
    }

    private static async Task RunTimedAsync(string name, TimeSpan duration, Func<Task> operation)
    {
        Console.WriteLine($"Running {name} for {duration}...");
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        long iterations = 0;
        while (sw.Elapsed < duration)
        {
            await operation();
            iterations++;
        }

        sw.Stop();
        long allocBytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        Console.WriteLine(
            $"{name}: {iterations} iterations in {sw.Elapsed} " +
            $"({sw.Elapsed.TotalMicroseconds / iterations:N2} us/op, " +
            $"{(double)allocBytes / iterations:N0} B/op allocated)");
    }
}
