using Quark.Abstractions;
using Quark.Abstractions.Streaming;
using Quark.Core.Actors;
using Quark.Core.Streaming;

namespace Quark.Examples.ReactiveActors;

/// <summary>
/// Example demonstrating reactive actors with windowing and stream operators.
/// Shows how to process streams with backpressure, aggregation, and transformations.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║    Quark Phase 10.1.3: Reactive Actors Example          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        await DemoReactiveAggregator();
        await DemoStreamOperators();
        await DemoWindowing();

        Console.WriteLine("\n╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              All Examples Complete!                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
    }

    /// <summary>
    /// Demonstrates a reactive actor that aggregates sensor data in time-based windows.
    /// </summary>
    static async Task DemoReactiveAggregator()
    {
        Console.WriteLine("═══ Example 1: Stream Aggregation with Time Windows ═══");
        Console.WriteLine("Aggregating sensor data every 2 seconds...\n");

        var actor = new SensorAggregatorActor("sensor-agg-1");
        var processTask = Task.Run(async () => await actor.StartProcessing());

        // Simulate sensor readings
        for (int i = 1; i <= 10; i++)
        {
            var reading = new SensorReading
            {
                SensorId = $"sensor-{i % 3 + 1}",
                Temperature = 20.0 + (i * 0.5),
                Timestamp = DateTime.UtcNow
            };

            await actor.SendAsync(reading);
            Console.WriteLine($"  [Sent] Sensor {reading.SensorId}: {reading.Temperature:F1}°C");
            await Task.Delay(200); // Simulate readings every 200ms
        }

        actor.CompleteInput();
        await processTask;

        Console.WriteLine($"\n✓ Processed {actor.MessagesProcessed} aggregated windows");
        Console.WriteLine();
    }

    /// <summary>
    /// Demonstrates stream operators (Map, Filter, Reduce) on a reactive actor.
    /// </summary>
    static async Task DemoStreamOperators()
    {
        Console.WriteLine("═══ Example 2: Stream Operators (Map, Filter, Reduce) ═══");
        Console.WriteLine("Processing numbers with transformations...\n");

        var actor = new NumberProcessorActor("number-proc-1");
        var processTask = Task.Run(async () => await actor.StartProcessing());

        // Send numbers
        for (int i = 1; i <= 20; i++)
        {
            await actor.SendAsync(i);
        }

        actor.CompleteInput();
        await processTask;

        Console.WriteLine($"\n✓ Processed {actor.Outputs.Count} outputs from stream operations");
        Console.WriteLine($"  Outputs: {string.Join(", ", actor.Outputs.Take(10))}...");
        Console.WriteLine();
    }

    /// <summary>
    /// Demonstrates different windowing strategies.
    /// </summary>
    static async Task DemoWindowing()
    {
        Console.WriteLine("═══ Example 3: Windowing Strategies ═══");
        Console.WriteLine("Testing count-based and sliding windows...\n");

        var actor = new WindowedProcessorActor("windowed-proc-1");
        var processTask = Task.Run(async () => await actor.StartProcessing());

        // Send a series of values
        for (int i = 1; i <= 15; i++)
        {
            await actor.SendAsync(i);
            await Task.Delay(50);
        }

        actor.CompleteInput();
        await processTask;

        Console.WriteLine($"\n✓ Processed {actor.WindowsProcessed} windows");
        Console.WriteLine();
    }
}

// ═══ Message Types ═══

/// <summary>
/// Represents a sensor reading with temperature data.
/// </summary>
public class SensorReading
{
    public string SensorId { get; set; } = "";
    public double Temperature { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Represents aggregated sensor statistics over a time window.
/// </summary>
public class AggregatedStats
{
    public int ReadingCount { get; set; }
    public double AverageTemperature { get; set; }
    public double MinTemperature { get; set; }
    public double MaxTemperature { get; set; }
    public TimeSpan WindowDuration { get; set; }

    public override string ToString()
    {
        return $"Count={ReadingCount}, Avg={AverageTemperature:F1}°C, Min={MinTemperature:F1}°C, Max={MaxTemperature:F1}°C, Duration={WindowDuration.TotalSeconds:F1}s";
    }
}

// ═══ Reactive Actor Implementations ═══

/// <summary>
/// Reactive actor that aggregates sensor readings in time-based windows.
/// </summary>
[Actor(Name = "SensorAggregator")]
[ReactiveActor(BufferSize = 1000, BackpressureThreshold = 0.8)]
public class SensorAggregatorActor : ReactiveActorBase<SensorReading, AggregatedStats>
{
    public SensorAggregatorActor(string actorId) : base(actorId)
    {
    }

    public override async IAsyncEnumerable<AggregatedStats> ProcessStreamAsync(
        IAsyncEnumerable<SensorReading> stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Use time-based windows to aggregate readings every 2 seconds
        await foreach (var window in stream.Window(TimeSpan.FromSeconds(2)).WithCancellation(cancellationToken))
        {
            var readings = window.Messages;
            if (readings.Count > 0)
            {
                var stats = new AggregatedStats
                {
                    ReadingCount = readings.Count,
                    AverageTemperature = readings.Average(r => r.Temperature),
                    MinTemperature = readings.Min(r => r.Temperature),
                    MaxTemperature = readings.Max(r => r.Temperature),
                    WindowDuration = window.EndTime - window.StartTime
                };

                yield return stats;
            }
        }
    }

    protected override Task OnOutputAsync(AggregatedStats output, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  [Aggregated] {output}");
        return Task.CompletedTask;
    }

    public Task StartProcessing(CancellationToken cancellationToken = default)
    {
        return StartStreamProcessingAsync(cancellationToken);
    }
}

/// <summary>
/// Reactive actor that demonstrates stream operators (Map, Filter, Reduce).
/// </summary>
[Actor(Name = "NumberProcessor")]
[ReactiveActor(BufferSize = 500)]
public class NumberProcessorActor : ReactiveActorBase<int, int>
{
    public readonly List<int> Outputs = new();

    public NumberProcessorActor(string actorId) : base(actorId)
    {
    }

    public override async IAsyncEnumerable<int> ProcessStreamAsync(
        IAsyncEnumerable<int> stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Chain operators: multiply by 2, filter evens, take first 10
        var processed = stream
            .Map(x => x * 2)           // Double each number
            .Filter(x => x % 4 == 0);  // Keep only multiples of 4

        await foreach (var value in processed.WithCancellation(cancellationToken))
        {
            yield return value;
        }
    }

    protected override Task OnOutputAsync(int output, CancellationToken cancellationToken = default)
    {
        Outputs.Add(output);
        return Task.CompletedTask;
    }

    public Task StartProcessing(CancellationToken cancellationToken = default)
    {
        return StartStreamProcessingAsync(cancellationToken);
    }
}

/// <summary>
/// Reactive actor that demonstrates count-based windowing.
/// </summary>
[Actor(Name = "WindowedProcessor")]
[ReactiveActor(BufferSize = 300)]
public class WindowedProcessorActor : ReactiveActorBase<int, int>
{
    public int WindowsProcessed { get; private set; }

    public WindowedProcessorActor(string actorId) : base(actorId)
    {
    }

    public override async IAsyncEnumerable<int> ProcessStreamAsync(
        IAsyncEnumerable<int> stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Use count-based windows (batches of 5)
        await foreach (var window in stream.Window(5).WithCancellation(cancellationToken))
        {
            WindowsProcessed++;
            var sum = window.Messages.Sum();
            Console.WriteLine($"  [Window {WindowsProcessed}] Items: {string.Join(", ", window.Messages)}, Sum: {sum}");
            yield return sum;
        }
    }

    public Task StartProcessing(CancellationToken cancellationToken = default)
    {
        return StartStreamProcessingAsync(cancellationToken);
    }
}
