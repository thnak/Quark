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
