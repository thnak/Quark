using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization.Abstractions.Attributes;
using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Quark.Testing.Harness;

namespace Quark.Performance;

/// <summary>
/// Quick local streaming test - runs without BenchmarkDotNet for fast iteration.
/// Run with: dotnet run --project tests/Quark.Performance -- LocalStreaming
/// </summary>
public class LocalStreamingTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== Local Streaming Test ===\n");

        await using var cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddMemoryStreams("test");
            };
        });

        var provider = cluster.PrimarySilo.Services.GetRequiredKeyedService<IStreamProvider>("test");
        var streamId = StreamId.Create("test-namespace", "test-key");
        var stream = provider.GetStream<StreamTestMsg>(streamId);

        // Test 1: Simple publish/subscribe
        Console.WriteLine("Test 1: Simple Publish/Subscribe");
        var receivedCount = 0;
        var handle = await stream.SubscribeAsync(new StreamTestObserver(msg =>
        {
            Interlocked.Increment(ref receivedCount);
        }));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            await stream.OnNextAsync(new StreamTestMsg { Id = i, Content = $"Event {i}" });
        }
        sw.Stop();

        // Give time for all events to be processed
        await Task.Delay(100);

        Console.WriteLine($"  Published 1000 events in {sw.ElapsedMilliseconds}ms ({1000.0 / sw.Elapsed.TotalSeconds:F0} events/sec)");
        Console.WriteLine($"  Received {receivedCount} events");
        Console.WriteLine();

        await handle.UnsubscribeAsync();

        // Test 2: Batch processing
        Console.WriteLine("Test 2: Batch Processing");

        var batchSizes = new[] { 10, 100, 1000 };
        foreach (var batchSize in batchSizes)
        {
            receivedCount = 0;
            handle = await stream.SubscribeAsync(new StreamTestObserver(msg =>
            {
                Interlocked.Increment(ref receivedCount);
            }));

            sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < batchSize; i++)
            {
                await stream.OnNextAsync(new StreamTestMsg { Id = i, Content = $"Batch {batchSize} - {i}" });
            }
            sw.Stop();

            await Task.Delay(50);

            Console.WriteLine($"  Batch size {batchSize}: {sw.ElapsedMilliseconds}ms ({batchSize / sw.Elapsed.TotalSeconds:F0} events/sec), received {receivedCount}");

            await handle.UnsubscribeAsync();
        }

        // Test 3: Multiple subscribers
        Console.WriteLine("\nTest 3: Multiple Subscribers");
        var subscriberCount = 5;
        var handles = new List<StreamSubscriptionHandle<StreamTestMsg>>();
        var counts = new int[subscriberCount];

        for (int i = 0; i < subscriberCount; i++)
        {
            var index = i;
            var h = await stream.SubscribeAsync(new StreamTestObserver(msg =>
            {
                Interlocked.Increment(ref counts[index]);
            }));
            handles.Add(h);
        }

        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 500; i++)
        {
            await stream.OnNextAsync(new StreamTestMsg { Id = i, Content = $"Multi {i}" });
        }
        sw.Stop();

        await Task.Delay(100);

        Console.WriteLine($"  {subscriberCount} subscribers, 500 events: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Received per subscriber: {string.Join(", ", counts)}");

        foreach (var h in handles)
        {
            await h.UnsubscribeAsync();
        }

        Console.WriteLine("\n=== Streaming Test Complete ===");
    }
}

[GenerateSerializer]
public class StreamTestMsg
{
    [Id(0)] public int Id { get; set; }
    [Id(1)] public string Content { get; set; } = "";
}

public class StreamTestObserver : IAsyncObserver<StreamTestMsg>
{
    private readonly Action<StreamTestMsg> _onReceived;

    public StreamTestObserver(Action<StreamTestMsg> onReceived)
    {
        _onReceived = onReceived;
    }

    public ValueTask OnNextAsync(StreamTestMsg item, StreamSequenceToken? token = null)
    {
        _onReceived(item);
        return default;
    }

    public ValueTask OnCompletedAsync() => default;
    public ValueTask OnErrorAsync(Exception ex) => default;
}
