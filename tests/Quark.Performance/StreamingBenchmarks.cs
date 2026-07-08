using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization.Abstractions.Attributes;
using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Quark.Testing.Harness;

namespace Quark.Performance;

/// <summary>
/// Streaming benchmarks using in-memory streams.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 3)]
[MemoryDiagnoser]
public class StreamingBenchmarks : IAsyncDisposable
{
    private TestCluster? _cluster;
    private IStreamProvider? _streamProvider;
    private IAsyncStream<BenchmarkMessage>? _stream;
    private StreamId _streamId;
    private int _receivedCount;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddMemoryStreams("test");
            };
        });

        _streamProvider = _cluster.PrimarySilo.Services.GetRequiredKeyedService<IStreamProvider>("test");
        _streamId = StreamId.Create("test-namespace", "test-key");
        _stream = _streamProvider.GetStream<BenchmarkMessage>(_streamId);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_cluster != null)
        {
            await _cluster.DisposeAsync();
        }
    }

    [Benchmark]
    public async Task StreamPublishSingle()
    {
        await _stream!.OnNextAsync(new BenchmarkMessage { Id = 1, Content = "Hello" });
    }

    [Benchmark]
    public async Task StreamPublishBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            await _stream!.OnNextAsync(new BenchmarkMessage { Id = i, Content = $"Message {i}" });
        }
    }

    [Benchmark]
    public async Task StreamSubscribeAndPublish()
    {
        _receivedCount = 0;
        var handle = await _stream!.SubscribeAsync(new BenchmarkObserver(msg =>
        {
            Interlocked.Increment(ref _receivedCount);
        }));

        for (int i = 0; i < 50; i++)
        {
            await _stream.OnNextAsync(new BenchmarkMessage { Id = i, Content = $"Multi {i}" });
        }

        await handle.UnsubscribeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }
}

[GenerateSerializer]
public class BenchmarkMessage
{
    [Id(0)] public int Id { get; set; }
    [Id(1)] public string Content { get; set; } = "";
}

public class BenchmarkObserver : IAsyncObserver<BenchmarkMessage>
{
    private readonly Action<BenchmarkMessage> _onReceived;

    public BenchmarkObserver(Action<BenchmarkMessage> onReceived)
    {
        _onReceived = onReceived;
    }

    public ValueTask OnNextAsync(BenchmarkMessage item, StreamSequenceToken? token = null)
    {
        _onReceived(item);
        return default;
    }

    public ValueTask OnCompletedAsync() => default;
    public ValueTask OnErrorAsync(Exception ex) => default;
}
