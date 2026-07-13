using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Performance.PingPong;
using Quark.Runtime;
using Quark.Serialization;

namespace Quark.Performance;

/// <summary>
///     Per-type allocation attribution for a single grain call, on the real
///     <see cref="LocalGrainCallInvoker"/> path. BenchmarkDotNet's MemoryDiagnoser reports total B/op but
///     not <em>which types</em> allocate; this drives the same dispatch in a tight single-threaded loop
///     under an in-process <c>GCAllocationTick</c> EventListener, so the residual after Part A
///     (activation-scoped) can be broken down by type and the next reduction targeted at ground truth.
///     Run: <c>dotnet run -c Release --project tests/Quark.Performance -- AllocByType [seconds]</c>.
/// </summary>
public static class AllocByTypeProfiler
{
    private static readonly GrainType PerCallType = new("PingPongGrain");
    private static readonly GrainType ActivationType = new("ActivationPingPongGrain");

    public static async Task RunAsync(string[] args)
    {
        double seconds = args.Length > 1 && double.TryParse(args[1], out double s) ? s : 5.0;

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
        await using ServiceProvider sp = services.BuildServiceProvider();

        GrainTypeRegistry typeReg = sp.GetRequiredService<GrainTypeRegistry>();
        typeReg.Register(PerCallType, typeof(PingPongGrainBehavior));
        typeReg.Register(ActivationType, typeof(ActivationPingPongGrainBehavior));

        var invoker = sp.GetRequiredService<IGrainCallInvoker>();
        GrainId perCall = GrainId.Create(PerCallType, "b0");
        GrainId activation = GrainId.Create(ActivationType, "b0");

        // Pre-activate so we measure steady-state calls, not the one-time activation.
        await invoker.InvokeVoidAsync(perCall, new PingPongBehavior_PingInvokable());
        await invoker.InvokeVoidAsync(activation, new PingPongBehavior_PingInvokable());

        Console.WriteLine($"=== Allocation-by-type: {seconds:F0}s per variant ===\n");
        await MeasureAsync("Per-call (IGrainBehavior)", invoker, perCall, seconds);
        await MeasureAsync("Activation-scoped (IActivationBehavior)", invoker, activation, seconds);
    }

    private static async Task MeasureAsync(string label, IGrainCallInvoker invoker, GrainId id, double seconds)
    {
        // Warm up (JIT + pools) outside the measured window.
        for (int i = 0; i < 50_000; i++)
        {
            await invoker.InvokeVoidAsync(id, new PingPongBehavior_PingInvokable());
        }

        using var listener = new AllocationTickListener();
        long calls = 0;
        long allocBefore = GC.GetTotalAllocatedBytes(precise: false);
        listener.Start();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            for (int i = 0; i < 1000; i++)
            {
                await invoker.InvokeVoidAsync(id, new PingPongBehavior_PingInvokable());
            }
            calls += 1000;
        }

        listener.Stop();
        long allocDelta = GC.GetTotalAllocatedBytes(precise: false) - allocBefore;

        Console.WriteLine($"--- {label} ---");
        Console.WriteLine($"  calls: {calls:N0}   total: {allocDelta:N0} B   per-call: {(double)allocDelta / calls:N1} B");
        long sampled = listener.TotalSampled;
        foreach ((string type, long bytes) in listener.Top(15))
        {
            double perCall = (double)bytes / calls;
            double pct = sampled > 0 ? 100.0 * bytes / sampled : 0;
            Console.WriteLine($"    {perCall,7:N1} B/call {pct,5:F1}%  {Short(type)}");
        }

        Console.WriteLine();
    }

    private static string Short(string typeName)
    {
        // Keep enough of the namespace to tell mailbox (System.Threading.Channels) apart from
        // scheduler/runtime allocators — the bare leaf name collides across both.
        string t = typeName;
        int bracket = t.IndexOf('[');
        string head = bracket >= 0 ? t[..bracket] : t;
        string tail = bracket >= 0 ? t[bracket..] : "";
        string[] parts = head.Split('.');
        string shortHead = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : head;
        return shortHead + tail;
    }

    // Counts GCAllocationTick bytes by allocated type name. AllocationTick fires roughly every ~100 KB
    // allocated of a given type, and its AllocationAmount payload is that delta — summing it per type
    // gives a proportional per-type allocation histogram, which MemoryDiagnoser's single total cannot.
    private sealed class AllocationTickListener : EventListener
    {
        private readonly ConcurrentDictionary<string, long> _byType = new();
        private volatile bool _enabled;

        public long TotalSampled { get; private set; }

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
            {
                // Keyword 0x1 = GC; AllocationTick is emitted at Verbose.
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (!_enabled || e.EventName is null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal))
            {
                return;
            }

            IReadOnlyList<string>? names = e.PayloadNames;
            IReadOnlyList<object?>? payload = e.Payload;
            if (names is null || payload is null)
            {
                return;
            }

            string type = "?";
            long amount = 0;
            for (int i = 0; i < names.Count; i++)
            {
                switch (names[i])
                {
                    case "TypeName":
                        type = payload[i]?.ToString() ?? "?";
                        break;
                    case "AllocationAmount64":
                        amount = payload[i] is not null ? Convert.ToInt64(payload[i]) : 0;
                        break;
                }
            }

            if (amount <= 0)
            {
                return;
            }

            _byType.AddOrUpdate(type, amount, (_, v) => v + amount);
        }

        public void Start() => _enabled = true;

        public void Stop()
        {
            _enabled = false;
            TotalSampled = _byType.Values.Sum();
        }

        public IEnumerable<KeyValuePair<string, long>> Top(int n)
            => _byType.OrderByDescending(kv => kv.Value).Take(n);
    }
}
