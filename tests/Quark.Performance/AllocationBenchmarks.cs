using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Performance.Shared;
using Quark.Runtime;
using Quark.Serialization;

namespace Quark.Performance;

/// <summary>
///     Allocation profile of grain calls under contention, isolating whether contention itself
///     (queueing behind one mailbox, thread-local pooled-item cache misses under concurrent access)
///     adds allocation beyond a single-caller baseline. Uses <see cref="Shared.IWorkGrain"/> directly
///     against a hand-built <see cref="ServiceProvider"/> + <see cref="IGrainCallInvoker"/> -- no
///     <c>TestCluster</c>, no proxy -- same pattern as <see cref="DispatchPipelineBenchmarks"/>.
///     BenchmarkDotNet's <see cref="MemoryDiagnoserAttribute"/> hooks process-level GC counters
///     around each iteration, so (unlike a hand-rolled thread-local counter) it does not have the
///     cross-thread-attribution problem noted on <c>ActorLifecycle</c>'s allocation measurement --
///     the right tool for allocation-under-concurrency.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class AllocationBenchmarks
{
    private const int FanOutWidth = 8;
    private static readonly GrainType WorkGrainType = new("AllocationWorkGrain");

    private ServiceProvider _sp = null!;
    private IGrainCallInvoker _invoker = null!;
    private GrainId _singleGrainId;
    private GrainId[] _fanOutGrainIds = null!;

    // GlobalSetup/GlobalCleanup are synchronous wrappers that block on the async work internally
    // rather than returning Task directly -- BenchmarkDotNet's InProcessEmit toolchain does not
    // reliably await a Task-returning [GlobalSetup]/[GlobalCleanup] (see DispatchPipelineBenchmarks).
    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    [GlobalCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    private async Task SetupAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkSerialization();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "alloc-bench";
            o.ServiceId = "alloc-bench";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddGrainBehavior<IWorkGrain, WorkGrainBehavior>();
        _sp = services.BuildServiceProvider();

        _sp.GetRequiredService<GrainTypeRegistry>().Register(WorkGrainType, typeof(WorkGrainBehavior));
        _invoker = _sp.GetRequiredService<IGrainCallInvoker>();

        _singleGrainId = GrainId.Create(WorkGrainType, "single");
        _fanOutGrainIds = new GrainId[FanOutWidth];
        for (int i = 0; i < FanOutWidth; i++)
        {
            _fanOutGrainIds[i] = GrainId.Create(WorkGrainType, $"fanout-{i}");
        }

        // Pre-activate every grain used below so measured calls hit steady state, not activation cost.
        await _invoker.InvokeAsync<WorkGrainBehavior_DoWorkInvokable, long>(_singleGrainId, new WorkGrainBehavior_DoWorkInvokable(0));
        foreach (GrainId id in _fanOutGrainIds)
        {
            await _invoker.InvokeAsync<WorkGrainBehavior_DoWorkInvokable, long>(id, new WorkGrainBehavior_DoWorkInvokable(0));
        }
    }

    private async Task CleanupAsync()
    {
        await _sp.GetRequiredService<GrainActivationTable>().DisposeAsync();
        await _sp.DisposeAsync();
    }

    // Baseline: one caller, one grain, no artificial work (microseconds: 0 isolates allocation
    // from spin time). Should roughly track DispatchPipelineBenchmarks.FullInvokeVoidAsync's
    // allocation profile -- not identical, since this exercises the typed-result
    // MailboxWorkItem<TState,TResult> path rather than the void one.
    [Benchmark(Baseline = true)]
    public async Task<long> SingleGrainSequential()
        => await _invoker.InvokeAsync<WorkGrainBehavior_DoWorkInvokable, long>(_singleGrainId, new WorkGrainBehavior_DoWorkInvokable(0));

    // 8 concurrent callers against the SAME grain -- isolates whether contention on one serialized
    // mailbox (queueing, thread-local pooled-item cache misses under concurrent access) adds
    // allocation beyond the sequential baseline above.
    [Benchmark]
    public async Task SingleGrainConcurrentContention()
    {
        var tasks = new Task<long>[FanOutWidth];
        for (int i = 0; i < FanOutWidth; i++)
        {
            tasks[i] = _invoker.InvokeAsync<WorkGrainBehavior_DoWorkInvokable, long>(_singleGrainId, new WorkGrainBehavior_DoWorkInvokable(0)).AsTask();
        }
        await Task.WhenAll(tasks);
    }

    // 8 concurrent callers against 8 DISTINCT grains -- isolates fan-out cost from same-grain
    // contention cost measured above.
    [Benchmark]
    public async Task NGrainFanOut()
    {
        var tasks = new Task<long>[FanOutWidth];
        for (int i = 0; i < FanOutWidth; i++)
        {
            tasks[i] = _invoker.InvokeAsync<WorkGrainBehavior_DoWorkInvokable, long>(_fanOutGrainIds[i], new WorkGrainBehavior_DoWorkInvokable(0)).AsTask();
        }
        await Task.WhenAll(tasks);
    }
}
