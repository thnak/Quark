using BenchmarkDotNet.Running;

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
