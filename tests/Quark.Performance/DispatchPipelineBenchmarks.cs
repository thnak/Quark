using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Diagnostics.Abstractions;
using Quark.Performance.PingPong;
using Quark.Runtime;
using Quark.Serialization;

namespace Quark.Performance;

/// <summary>
/// Isolates each stage of LocalGrainCallInvoker's dispatch path so the gap to Akka's ping-pong
/// figure (see docs/superpowers/specs/2026-07-09-dispatch-pipeline-benchmark-design.md) can be
/// attributed to specific stages instead of guessed at.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class DispatchPipelineBenchmarks
{
    private static readonly GrainType PingPongGrainType = new("PingPongGrain");
    private static readonly Func<ValueTask<GrainActivation>> ThrowingFactory =
        static () => throw new InvalidOperationException("Factory should not run — activation must already exist.");
    private static readonly Func<ValueTask> NoOpWorkItem = static () => default;

    private ServiceProvider _sp = null!;
    private GrainActivationTable _activationTable = null!;
    private IGrainCallInvoker _invoker = null!;
    private GrainId _grainId;
    private GrainActivation _bareActivation = null!;
    private GrainActivation _bareActivationReentrant = null!;

    private ServiceProvider _spDiag = null!;
    private IGrainCallInvoker _invokerDiag = null!;
    private GrainId _grainIdDiag;

    private Channel<TaskCompletionSource> _signalChannel = null!;
    private Channel<byte> _noSignalChannel = null!;
    private CancellationTokenSource _readerCts = null!;
    private Task _signalReaderTask = null!;
    private Task _noSignalReaderTask = null!;

    // GlobalSetup/GlobalCleanup are synchronous wrappers that block on the async work internally
    // rather than returning Task directly — BenchmarkDotNet's InProcessEmit toolchain does not
    // reliably await a Task-returning [GlobalSetup]/[GlobalCleanup], which races the benchmark
    // workload against unfinished setup (observed: ChannelSignalPattern's late-initialized channel
    // fields were still null when the workload started).
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
            o.ClusterId = "bench";
            o.ServiceId = "bench";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
        _sp = services.BuildServiceProvider();

        _sp.GetRequiredService<GrainTypeRegistry>().Register(PingPongGrainType, typeof(PingPongGrainBehavior));

        _activationTable = _sp.GetRequiredService<GrainActivationTable>();
        _invoker = _sp.GetRequiredService<IGrainCallInvoker>();
        _grainId = GrainId.Create(PingPongGrainType, "bench-0");

        // Pre-activate so the table-lookup and full-invoke benchmarks hit steady state.
        await _invoker.InvokeVoidAsync(_grainId, new PingPongBehavior_PingInvokable());

        // Bare activations, constructed directly (bypassing GrainActivationTable) so mailbox-only
        // stages measure the channel/scheduler cost in isolation from DI/behavior resolution.
        _bareActivation = new GrainActivation(
            GrainId.Create(PingPongGrainType, "bare-non-reentrant"),
            PingPongGrainType,
            isReentrant: false,
            _sp,
            _sp.GetRequiredService<ILogger<GrainActivation>>(),
            _sp.GetRequiredService<IActivationScheduler>());

        _bareActivationReentrant = new GrainActivation(
            GrainId.Create(PingPongGrainType, "bare-reentrant"),
            PingPongGrainType,
            isReentrant: true,
            _sp,
            _sp.GetRequiredService<ILogger<GrainActivation>>(),
            _sp.GetRequiredService<IActivationScheduler>());

        var diagServices = new ServiceCollection();
        diagServices.AddLogging();
        diagServices.AddQuarkSerialization();
        diagServices.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "bench-diag";
            o.ServiceId = "bench-diag";
            o.SiloName = "silo0";
        });
        diagServices.AddSingleton<IQuarkDiagnosticListener, CountingDiagnosticListener>();
        diagServices.AddQuarkRuntime();
        diagServices.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
        _spDiag = diagServices.BuildServiceProvider();
        _spDiag.GetRequiredService<GrainTypeRegistry>().Register(PingPongGrainType, typeof(PingPongGrainBehavior));
        _invokerDiag = _spDiag.GetRequiredService<IGrainCallInvoker>();
        _grainIdDiag = GrainId.Create(PingPongGrainType, "bench-diag-0");
        await _invokerDiag.InvokeVoidAsync(_grainIdDiag, new PingPongBehavior_PingInvokable());

        _readerCts = new CancellationTokenSource();
        _signalChannel = Channel.CreateUnbounded<TaskCompletionSource>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        _noSignalChannel = Channel.CreateUnbounded<byte>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        _signalReaderTask = Task.Run(() => DrainSignalChannelAsync(_readerCts.Token));
        _noSignalReaderTask = Task.Run(() => DrainNoSignalChannelAsync(_readerCts.Token));
    }

    private async Task CleanupAsync()
    {
        await _readerCts.CancelAsync();
        _signalChannel.Writer.TryComplete();
        _noSignalChannel.Writer.TryComplete();
        await _signalReaderTask;
        await _noSignalReaderTask;
        _readerCts.Dispose();

        await _bareActivationReentrant.DisposeAsync();
        await _bareActivation.DisposeAsync();

        await _spDiag.GetRequiredService<GrainActivationTable>().DisposeAsync();
        await _activationTable.DisposeAsync();

        await _sp.DisposeAsync();
        await _spDiag.DisposeAsync();
    }

    // Stage 1: GrainActivationTable.GetOrCreateAsync steady-state lookup.
    [Benchmark]
    public async ValueTask<GrainActivation> ActivationTableLookup()
        => await _activationTable.GetOrCreateAsync(_grainId, ThrowingFactory);

    // Stage 2: bare IServiceScope create+dispose, no resolution.
    [Benchmark]
    public void ServiceScopeCreateDispose()
    {
        using IServiceScope scope = _sp.CreateScope();
    }

    // Stage 3: GrainScopeBinder.BindAndResolveAsync — the 4 DI resolutions + behavior resolve.
    [Benchmark]
    public async ValueTask<IGrainBehavior> ScopeBindAndResolve()
    {
        using IServiceScope scope = _sp.CreateScope();
        return await GrainScopeBinder.BindAndResolveAsync(scope.ServiceProvider, _bareActivation, default);
    }

    // Stage 4: ExecutionContext.Capture() alone (called on every PostAsync).
    [Benchmark]
    public ExecutionContext? ExecutionContextCapture()
        => ExecutionContext.Capture();

    // Stage 5: mailbox round trip, non-reentrant — real Channel write + scheduler wake +
    // pooled-item completion signal (forced onto the thread pool).
    [Benchmark]
    public async ValueTask MailboxRoundTrip()
        => await _bareActivation.PostAsync(NoOpWorkItem);

    // Stage 6: same call, but isReentrant: true — bypasses the channel/scheduler entirely
    // (inline execution). The delta vs MailboxRoundTrip is the channel+scheduler+thread-hop cost.
    [Benchmark]
    public async ValueTask MailboxRoundTripReentrant()
        => await _bareActivationReentrant.PostAsync(NoOpWorkItem);

    // Stage 7a: complete InvokeVoidAsync, NullDiagnosticListener (default — no-op).
    [Benchmark]
    public async ValueTask FullInvokeDiagnosticsOff()
        => await _invoker.InvokeVoidAsync(_grainId, new PingPongBehavior_PingInvokable());

    // Stage 7b: same call, with a listener that does real work (Interlocked.Increment), not a no-op.
    [Benchmark]
    public async ValueTask FullInvokeDiagnosticsOn()
        => await _invokerDiag.InvokeVoidAsync(_grainIdDiag, new PingPongBehavior_PingInvokable());

    // Stage 8: the complete real number, single-threaded ops/sec — should roughly equal
    // stage 1 + stage 3 + stage 5 combined. Distinctly named per the design's summary table.
    [Benchmark]
    public async ValueTask FullInvokeVoidAsync()
        => await _invoker.InvokeVoidAsync(_grainId, new PingPongBehavior_PingInvokable());

    // Stage 9a: write + await a forced-async completion signal — Quark's real RPC-await pattern.
    [Benchmark]
    public async Task ChannelSignalPattern()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _signalChannel.Writer.WriteAsync(tcs);
        await tcs.Task;
    }

    // Stage 9b: write and return immediately — no completion wait. Not a call into production
    // fire-and-forget code (Quark has none — see spec §2); a synthetic pattern-level comparison.
    [Benchmark]
    public async ValueTask ChannelNoSignalPattern()
        => await _noSignalChannel.Writer.WriteAsync(0);

    private async Task DrainSignalChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (TaskCompletionSource tcs in _signalChannel.Reader.ReadAllAsync(ct))
            {
                tcs.SetResult();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainNoSignalChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (byte _ in _noSignalChannel.Reader.ReadAllAsync(ct))
            {
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class CountingDiagnosticListener : IQuarkDiagnosticListener
    {
        private long _count;
        public void OnInvocationStart(in InvocationStartEvent e) => Interlocked.Increment(ref _count);
        public void OnInvocationEnd(in InvocationEndEvent e) => Interlocked.Increment(ref _count);
    }
}
