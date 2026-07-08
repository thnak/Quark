using BenchmarkDotNet.Attributes;
using Quark.Core.Abstractions.Grains;

namespace Quark.Performance;

public interface ICounterGrain : IGrain
{
    ValueTask<int> IncrementAsync();
    ValueTask<int> GetCountAsync();
    ValueTask ResetAsync();
}

public interface IHelloGrain : IGrain
{
    ValueTask<string> SayHelloAsync(string name);
}

public class CounterGrainBehavior : IGrainBehavior, ICounterGrain
{
    private int _count;

    public ValueTask<int> IncrementAsync()
    {
        _count++;
        return new ValueTask<int>(_count);
    }

    public ValueTask<int> GetCountAsync() => new(_count);

    public ValueTask ResetAsync()
    {
        _count = 0;
        return default;
    }
}

public class HelloGrainBehavior : IGrainBehavior, IHelloGrain
{
    public ValueTask<string> SayHelloAsync(string name)
    {
        return new ValueTask<string>($"Hello, {name}!");
    }
}

/// <summary>
/// Benchmarks for grain behavior method call performance (direct behavior invocation).
/// These measure the overhead of behavior method dispatch without full grain activation.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class GrainCallBenchmarks
{
    private readonly CounterGrainBehavior _counter = new();
    private readonly HelloGrainBehavior _hello = new();

    [Benchmark]
    public async Task<int> CounterIncrement()
    {
        return await _counter.IncrementAsync();
    }

    [Benchmark]
    public async Task<string> HelloGrainCall()
    {
        return await _hello.SayHelloAsync("Benchmark");
    }

    [Benchmark]
    public async Task CounterMultipleIncrements()
    {
        for (int i = 0; i < 100; i++)
        {
            await _counter.IncrementAsync();
        }
    }
}
