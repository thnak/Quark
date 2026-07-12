using BenchmarkDotNet.Running;
using Quark.Performance.ActorLifecycle;
using Quark.Performance.AstroSim;
using Quark.Performance.Backpressure;
using Quark.Performance.CoreScalability;
using Quark.Performance.Fairness;
using Quark.Performance.MailboxContention;
using Quark.Performance.PingPong;
using Quark.Performance.SchedulerSkew;
using Quark.Performance.SchedulingQuality;

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

        // Check if user wants to run the scheduler skew benchmark
        if (args.Length > 0 && args[0].Equals("SchedulerSkew", StringComparison.OrdinalIgnoreCase))
        {
            await SchedulerSkewRunner.RunAsync(args);
            return;
        }

        // Check if user wants to run the mailbox contention benchmark
        if (args.Length > 0 && args[0].Equals("MailboxContention", StringComparison.OrdinalIgnoreCase))
        {
            await MailboxContentionRunner.RunAsync(args);
            return;
        }

        // Check if user wants to run the fairness benchmark
        if (args.Length > 0 && args[0].Equals("Fairness", StringComparison.OrdinalIgnoreCase))
        {
            await FairnessRunner.RunAsync(args);
            return;
        }

        // Check if user wants to run the scheduling quality benchmark
        if (args.Length > 0 && args[0].Equals("SchedulingQuality", StringComparison.OrdinalIgnoreCase))
        {
            await SchedulingQualityRunner.RunAsync(args);
            return;
        }

        // Check if user wants to run the actor lifecycle benchmark
        if (args.Length > 0 && args[0].Equals("ActorLifecycle", StringComparison.OrdinalIgnoreCase))
        {
            await ActorLifecycleRunner.RunAsync(args);
            return;
        }

        // Check if user wants to run the backpressure benchmark
        if (args.Length > 0 && args[0].Equals("Backpressure", StringComparison.OrdinalIgnoreCase))
        {
            await BackpressureRunner.RunAsync(args);
            return;
        }

        // Check if user wants to run the core-scalability benchmark
        if (args.Length > 0 && args[0].Equals("CoreScalability", StringComparison.OrdinalIgnoreCase))
        {
            await CoreScalabilityRunner.RunAsync(args);
            return;
        }

        // Otherwise run BenchmarkDotNet benchmarks
        var switcher = new BenchmarkSwitcher(new[]
        {
            typeof(GrainCallBenchmarks),
            typeof(StreamingBenchmarks),
            typeof(SerializationBenchmarks),
            typeof(DispatchPipelineBenchmarks),
            typeof(AllocationBenchmarks),
            typeof(CacheLocalityBenchmarks),
            typeof(SchedulerShardDistributionBenchmarks),
            typeof(ActivationLifecycleBenchmarks),
        });

        switcher.Run(args);
    }
}
