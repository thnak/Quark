using BenchmarkDotNet.Attributes;
using Quark.Core.Abstractions.Identity;
using Quark.Performance.Shared;

namespace Quark.Performance;

/// <summary>
///     Part (a): demonstrates false-sharing cost directly -- an unpadded shared-counter-array
///     baseline vs. <see cref="PaddedCounter"/>, under concurrent increment load. Generalizes the
///     <c>PaddedCounter</c> lesson already used (as a code comment) by
///     <c>PingPong/PingPongRunner.cs</c> into a measured wall-clock number. Both variants allocate
///     ~nothing, so the interesting column here is Mean/Median time, not Allocated.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class CacheLocalityBenchmarks
{
    private const int IncrementsPerThread = 1_000_000;

    [Params(2, 4, 8)]
    public int ThreadCount { get; set; }

    private long[] _unpadded = null!;
    private PaddedCounter[] _padded = null!;

    [GlobalSetup]
    public void Setup()
    {
        _unpadded = new long[ThreadCount];
        _padded = new PaddedCounter[ThreadCount];
    }

    [Benchmark(Baseline = true)]
    public void UnpaddedConcurrentIncrement()
        => Parallel.For(0, ThreadCount, i =>
        {
            for (int j = 0; j < IncrementsPerThread; j++)
            {
                Interlocked.Increment(ref _unpadded[i]);
            }
        });

    [Benchmark]
    public void PaddedConcurrentIncrement()
        => Parallel.For(0, ThreadCount, i =>
        {
            for (int j = 0; j < IncrementsPerThread; j++)
            {
                Interlocked.Increment(ref _padded[i].Value);
            }
        });
}

/// <summary>
///     Part (b): measures <c>ActivationScheduler</c>'s shard-hashing distribution. The real
///     <c>ShardFor</c> method (src/Quark.Runtime/ActivationScheduler.cs) is private, so this
///     replicates its documented formula as a local pure function rather than reaching into
///     internals -- if that formula ever changes, this copy will silently drift, so a future change
///     to the scheduler's shard assignment should prompt a look here too.
///     Shard imbalance is a distributional property, not a timing, so BenchmarkDotNet's Mean/
///     Allocated columns can't surface it -- it's computed once per [Params] combination inside
///     <see cref="Setup"/> and printed directly (a visible side effect in BDN's captured output).
///     The <see cref="ShardHashComputation"/> benchmark itself times the legitimate throughput
///     question: how cheap the scheduler's per-reschedule shard recomputation is.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class SchedulerShardDistributionBenchmarks
{
    private static readonly GrainType ProbeGrainType = new("ShardDistributionProbeGrain");

    [Params(1000, 10000, 100000)]
    public int GrainCount { get; set; }

    [Params(4, 8, 16)]
    public int ShardCount { get; set; }

    private int[] _hashes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _hashes = new int[GrainCount];
        for (int i = 0; i < GrainCount; i++)
        {
            _hashes[i] = GrainId.Create(ProbeGrainType, $"grain-{i}").GetHashCode();
        }

        var counts = new int[ShardCount];
        foreach (int hash in _hashes)
        {
            counts[ShardFor(hash, ShardCount)]++;
        }

        double mean = (double)GrainCount / ShardCount;
        int max = counts.Max();
        Console.WriteLine($"  [ShardDistribution] grains={GrainCount} shards={ShardCount} mean={mean:F1} max={max} imbalance={max / mean:F2}x");
    }

    private static int ShardFor(int hash, int shardCount) => (hash & 0x7FFFFFFF) % shardCount;

    [Benchmark]
    public int ShardHashComputation()
    {
        int touched = 0;
        foreach (int hash in _hashes)
        {
            touched += ShardFor(hash, ShardCount);
        }
        return touched;
    }
}
