using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Quark.Testing.Harness;

namespace Quark.Performance;

/// <summary>
/// Benchmarks stream fan-out (one publish, N subscribers) against subscriber count.
/// StreamSubscriptionRegistry.PublishAsync (src/Quark.Streaming.InMemory/StreamSubscriptionRegistry.cs)
/// dispatches to every subscriber concurrently via Task.WhenAll, not sequentially -- this suite both
/// measures the per-publish plumbing overhead (snapshot copy, Task list build, WhenAll) as subscriber
/// count scales, and demonstrates the concurrency payoff: with subscribers that each await a fixed
/// real delay (simulating I/O), publish latency should track that one delay regardless of subscriber
/// count, not subscriber count times the delay.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class StreamFanOutBenchmarks : IAsyncDisposable
{
    [Params(1, 10, 100, 1000)]
    public int SubscriberCount { get; set; }

    private TestCluster? _noOpCluster;
    private IAsyncStream<BenchmarkMessage>? _noOpStream;
    private List<StreamSubscriptionHandle<BenchmarkMessage>> _noOpHandles = [];

    private TestCluster? _yieldCluster;
    private IAsyncStream<BenchmarkMessage>? _yieldStream;
    private List<StreamSubscriptionHandle<BenchmarkMessage>> _yieldHandles = [];

    private readonly BenchmarkMessage _message = new() { Id = 1, Content = "fan-out" };

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _noOpCluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services => services.AddMemoryStreams("fanout-noop");
        });
        IStreamProvider noOpProvider =
            _noOpCluster.PrimarySilo.Services.GetRequiredKeyedService<IStreamProvider>("fanout-noop");
        _noOpStream = noOpProvider.GetStream<BenchmarkMessage>(StreamId.Create("fanout-noop-ns", "k"));
        _noOpHandles = new List<StreamSubscriptionHandle<BenchmarkMessage>>(SubscriberCount);
        for (int i = 0; i < SubscriberCount; i++)
        {
            _noOpHandles.Add(await _noOpStream.SubscribeAsync(new NoOpObserver()));
        }

        _yieldCluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services => services.AddMemoryStreams("fanout-yield");
        });
        IStreamProvider yieldProvider =
            _yieldCluster.PrimarySilo.Services.GetRequiredKeyedService<IStreamProvider>("fanout-yield");
        _yieldStream = yieldProvider.GetStream<BenchmarkMessage>(StreamId.Create("fanout-yield-ns", "k"));
        _yieldHandles = new List<StreamSubscriptionHandle<BenchmarkMessage>>(SubscriberCount);
        for (int i = 0; i < SubscriberCount; i++)
        {
            _yieldHandles.Add(await _yieldStream.SubscribeAsync(new AsyncDelayObserver()));
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        foreach (StreamSubscriptionHandle<BenchmarkMessage> handle in _noOpHandles)
        {
            await handle.UnsubscribeAsync();
        }

        foreach (StreamSubscriptionHandle<BenchmarkMessage> handle in _yieldHandles)
        {
            await handle.UnsubscribeAsync();
        }

        if (_noOpCluster != null) await _noOpCluster.DisposeAsync();
        if (_yieldCluster != null) await _yieldCluster.DisposeAsync();
    }

    // Isolates the fan-out plumbing itself (subscriber snapshot copy, per-subscriber Task list,
    // Task.WhenAll) -- subscribers do zero work, so any growth as SubscriberCount scales is pure
    // dispatch overhead, not subscriber-side cost.
    [Benchmark(Baseline = true)]
    public async Task PublishToFanOut_NoOpSubscribers()
        => await _noOpStream!.OnNextAsync(_message);

    // Every subscriber awaits a fixed, identical real delay (simulating I/O -- a network push, a
    // disk write). Because PublishAsync fans out via Task.WhenAll rather than a sequential await
    // loop, mean latency here should track ~SubscriberDelayMs regardless of SubscriberCount, not
    // SubscriberCount x SubscriberDelayMs -- that gap IS the concurrency payoff. (A prior version of
    // this benchmark used Task.Yield() per subscriber instead of a fixed delay: that only measures
    // thread-pool scheduling overhead, which does scale with SubscriberCount, and so doesn't
    // actually demonstrate this property -- kept as a lesson in the commit history, not the code.)
    [Benchmark]
    public async Task PublishToFanOut_AsyncSubscribers()
        => await _yieldStream!.OnNextAsync(_message);

    public async ValueTask DisposeAsync() => await GlobalCleanup();

    private const int SubscriberDelayMs = 2;

    private sealed class NoOpObserver : IAsyncObserver<BenchmarkMessage>
    {
        public ValueTask OnNextAsync(BenchmarkMessage item, StreamSequenceToken? token = null) => default;
        public ValueTask OnErrorAsync(Exception ex) => default;
        public ValueTask OnCompletedAsync() => default;
    }

    private sealed class AsyncDelayObserver : IAsyncObserver<BenchmarkMessage>
    {
        public async ValueTask OnNextAsync(BenchmarkMessage item, StreamSequenceToken? token = null)
            => await Task.Delay(SubscriberDelayMs);

        public ValueTask OnErrorAsync(Exception ex) => default;
        public ValueTask OnCompletedAsync() => default;
    }
}
