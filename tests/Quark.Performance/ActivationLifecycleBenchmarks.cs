using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Performance.ActorLifecycle;
using Quark.Runtime;
using Quark.Serialization;

namespace Quark.Performance;

/// <summary>
///     Precise, BenchmarkDotNet-measured allocation cost of grain activation and deactivation in
///     isolation, complementing <c>ActorLifecycle</c>'s standalone-runner throughput/latency view.
///     Reuses <c>ActorLifecycle</c>'s no-op <see cref="IActivationLifecycle"/> grain (its only job is
///     to make real <c>OnActivateAsync</c>/<c>OnDeactivateAsync</c> hooks fire) against a hand-built
///     <see cref="ServiceProvider"/> + direct <see cref="IGrainCallInvoker"/>/<see cref="GrainActivationTable"/>
///     calls -- same pattern as <see cref="DispatchPipelineBenchmarks"/> and <see cref="AllocationBenchmarks"/>.
///     BenchmarkDotNet's <see cref="MemoryDiagnoserAttribute"/> hooks process-level GC counters, giving
///     an exact bytes/op with none of the cross-thread-attribution noise documented against
///     <c>ActorLifecycle --allocations</c>'s hand-rolled <c>GC.GetTotalAllocatedBytes</c> check (see
///     docs/superpowers/specs/2026-07-10-runtime-quality-benchmark-design.md §7) -- the right tool
///     when precise activation/deactivation allocation numbers matter more than a live throughput view.
/// </summary>
[SimpleJob(RunStrategy.ColdStart, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MemoryDiagnoser]
public class ActivationLifecycleBenchmarks
{
    private static readonly GrainType LifecycleGrainType = new("ActivationLifecycleBenchGrain");

    private ServiceProvider _sp = null!;
    private GrainActivationTable _activationTable = null!;
    private IGrainCallInvoker _invoker = null!;
    private long _seq;
    private GrainId _pendingDeactivateId;

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
            o.ClusterId = "activation-lifecycle-bench";
            o.ServiceId = "activation-lifecycle-bench";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddGrainBehavior<IActorLifecycleGrain, ActorLifecycleGrainBehavior>();
        _sp = services.BuildServiceProvider();

        _sp.GetRequiredService<GrainTypeRegistry>().Register(LifecycleGrainType, typeof(ActorLifecycleGrainBehavior));
        _activationTable = _sp.GetRequiredService<GrainActivationTable>();
        _invoker = _sp.GetRequiredService<IGrainCallInvoker>();

        await Task.CompletedTask;
    }

    private async Task CleanupAsync()
    {
        await _activationTable.DisposeAsync();
        await _sp.DisposeAsync();
    }

    private GrainId NextGrainId(string prefix) => GrainId.Create(LifecycleGrainType, $"{prefix}-{Interlocked.Increment(ref _seq)}");

    // Isolates activation cost alone: a fresh GrainId every invocation forces a genuinely new
    // GrainActivationTable.GetOrCreateAsync + OnActivateAsync, never a warm reused activation.
    // Left deactivated at the end of each measured call -- GlobalCleanup tears down whatever
    // accumulates via GrainActivationTable.DisposeAsync().
    [Benchmark]
    public async Task GrainActivate()
        => await _invoker.InvokeVoidAsync(NextGrainId("activate"), new ActorLifecycleBehavior_PingInvokable());

    // RunStrategy.ColdStart guarantees exactly one measured invocation per iteration, so this
    // IterationSetup -- which pre-activates a fresh grain immediately before the timed call --
    // truly runs once per measured GrainDeactivate call, isolating deactivation cost alone from
    // the activation cost that produced the grain being torn down.
    [IterationSetup(Target = nameof(GrainDeactivate))]
    public void PrepareDeactivate()
    {
        _pendingDeactivateId = NextGrainId("deactivate");
        _invoker.InvokeVoidAsync(_pendingDeactivateId, new ActorLifecycleBehavior_PingInvokable())
            .AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task GrainDeactivate()
        => await _activationTable.TryDeactivateAsync(_pendingDeactivateId);

    // The real end-to-end cost: activate, one call, deactivate -- the BenchmarkDotNet-precise
    // equivalent of ActorLifecycleRunner's --allocations round trip.
    [Benchmark]
    public async Task GrainActivateDeactivateRoundTrip()
    {
        GrainId id = NextGrainId("roundtrip");
        await _invoker.InvokeVoidAsync(id, new ActorLifecycleBehavior_PingInvokable());
        await _activationTable.TryDeactivateAsync(id);
    }
}
