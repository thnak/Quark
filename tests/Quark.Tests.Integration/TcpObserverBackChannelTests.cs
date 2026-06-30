using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;
using Quark.Transport.Tcp;
using Xunit;

namespace Quark.Tests.Integration;

/// <summary>
///     End-to-end tests for the TCP back-channel that lets silos invoke <see cref="IGrainObserver" />
///     callbacks on remote clients (issue #17).
/// </summary>
[Collection("GatewayTests")]
[Trait("category", "gateway")]
public sealed class TcpObserverBackChannelTests : IAsyncLifetime
{
    private const int SiloPort    = 11202;
    private const int GatewayPort = 30102;
    private readonly string _clusterId = $"obs-test-{Guid.NewGuid():N}";

    private IHost _siloHost = null!;
    private IHost _clientHost = null!;
    private readonly CaptureLoggerProvider _logCapture = new();

    public async Task InitializeAsync()
    {
        Console.WriteLine($"[TcpObserver] InitializeAsync start — ports {SiloPort}/{GatewayPort}");
        _siloHost = Host.CreateDefaultBuilder()
            .UseQuark(silo =>
            {
                silo.Services.AddQuarkRuntime();
                silo.Services.AddGrainBehavior<IVehicleGrain, VehicleGrainBehavior>();
                silo.Services.AddScoped<IActivationMemory<VehicleGrainState>>(sp =>
                    new ActivationMemoryAccessor<VehicleGrainState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<VehicleGrainState>()));

                silo.Services.AddGrainTransportDispatcher(
                    new GrainType("VehicleGrain"), VehicleGrain_TransportDispatcher.Instance);

                silo.UseLocalhostClustering(siloPort: SiloPort, gatewayPort: GatewayPort,
                    clusterId: _clusterId);
                silo.Services.AddTcpTransport();

                // Silo needs IGrainFactory with observer-proxy support for GetObserverRef<T>.
                silo.Services.AddLocalClusterClient();
                silo.Services.AddGrainProxy<IVehicleGrain, VehicleGrainProxy>();
                silo.Services.AddObserverProxy<IPositionObserver, PositionObserverProxy>();
            })
            .Build();

        _clientHost = Host.CreateDefaultBuilder()
            .ConfigureLogging(lb => lb.AddProvider(_logCapture))
            .UseQuarkClient(client =>
            {
                client.UseLocalhostGateway(GatewayPort);
                client.Services.AddGrainProxy<IVehicleGrain, VehicleGrainProxy>();
                client.AddObserverProxy<IPositionObserver, PositionObserverProxy>();
                client.AddObserverTransportDispatcher<IPositionObserver>(
                    PositionObserver_TransportDispatcher.Instance);
            })
            .Build();

        await _siloHost.StartAsync();
        Console.WriteLine("[TcpObserver] Silo started");
        await _clientHost.StartAsync();
        Console.WriteLine("[TcpObserver] Client started");
    }

    public async Task DisposeAsync()
    {
        await _clientHost.StopAsync();
        await _siloHost.StopAsync();
        _clientHost.Dispose();
        _siloHost.Dispose();
    }

    // -------------------------------------------------------------------------
    // Test — observer callback travels back over TCP from silo to client
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Observer_Callback_Reaches_Client_Over_TCP()
    {
        Console.WriteLine("[TcpObserver] Observer_Callback_Reaches_Client_Over_TCP start");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        IGrainFactory factory = _clientHost.Services.GetRequiredService<IGrainFactory>();
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();

        var recorder = new PositionRecorder();
        IPositionObserver observerRef = factory.CreateObjectReference<IPositionObserver>(recorder);

        // Give the silo a moment to receive the ObserverRegister frame.
        await Task.Delay(50, cts.Token);

        Console.WriteLine("[TcpObserver] Calling Subscribe...");
        IVehicleGrain vehicle = client.GetGrain<IVehicleGrain>("truck-1");
        await vehicle.Subscribe(observerRef).WaitAsync(cts.Token);
        Console.WriteLine("[TcpObserver] Subscribe done. Calling UpdatePosition...");
        await vehicle.UpdatePosition("moving-north").WaitAsync(cts.Token);
        Console.WriteLine("[TcpObserver] UpdatePosition done. Waiting for callback...");

        string? received = await recorder.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("moving-north", received);
    }

    [Fact]
    public async Task Multiple_Observer_Callbacks_All_Delivered()
    {
        Console.WriteLine("[TcpObserver] Multiple_Observer_Callbacks_All_Delivered start");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        IGrainFactory factory = _clientHost.Services.GetRequiredService<IGrainFactory>();
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();

        var recorder = new PositionRecorder(expected: 3);
        IPositionObserver observerRef = factory.CreateObjectReference<IPositionObserver>(recorder);
        await Task.Delay(50, cts.Token);

        Console.WriteLine("[TcpObserver] Multiple: Calling Subscribe...");
        IVehicleGrain vehicle = client.GetGrain<IVehicleGrain>("truck-2");
        await vehicle.Subscribe(observerRef).WaitAsync(cts.Token);
        Console.WriteLine("[TcpObserver] Multiple: Subscribe done.");
        await vehicle.UpdatePosition("north").WaitAsync(cts.Token);
        await vehicle.UpdatePosition("east").WaitAsync(cts.Token);
        await vehicle.UpdatePosition("south").WaitAsync(cts.Token);

        await recorder.WaitAllAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["north", "east", "south"], recorder.AllPositions);
    }

    // -------------------------------------------------------------------------
    // Test — a slow observer callback must NOT block the grain-call Response that
    // follows it on the same socket (issue #49 — head-of-line blocking).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Slow_Observer_Callback_Does_Not_Block_Grain_Call_Response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        IGrainFactory factory = _clientHost.Services.GetRequiredService<IGrainFactory>();
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gated = new GatedRecorder(gate.Task, invoked);
        IPositionObserver observerRef = factory.CreateObjectReference<IPositionObserver>(gated);
        await Task.Delay(50, cts.Token);

        IVehicleGrain vehicle = client.GetGrain<IVehicleGrain>("truck-gated");
        try
        {
            await vehicle.Subscribe(observerRef).WaitAsync(cts.Token);

            // UpdatePosition emits an ObserverInvoke frame and then its Response on the same
            // socket. The observer handler blocks on `gate`. With head-of-line blocking the
            // client read loop would stall inside the observer dispatch and never read the
            // Response — so this WaitAsync would time out. Decoupled dispatch lets the Response
            // through while the observer handler is still blocked.
            Task updateTask = vehicle.UpdatePosition("blocked");
            await updateTask.WaitAsync(TimeSpan.FromSeconds(3), cts.Token);

            // The grain call returned; the observer handler must still be mid-flight (blocked).
            await invoked.Task.WaitAsync(TimeSpan.FromSeconds(3), cts.Token);
        }
        finally
        {
            // Always release so the observer dispatch (and host teardown) can complete,
            // even if the assertions above failed/timed out.
            gate.TrySetResult();
        }
    }

    // -------------------------------------------------------------------------
    // Test — an observer callback that THROWS must not crash the client read loop
    // or fault unrelated pending grain RPCs (issue #20). Before the back-channel
    // decoupling, the uncaught exception hit FaultAllPending and cancelled every
    // in-flight call on the connection.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Throwing_Observer_Callback_Does_Not_Fault_Pending_Grain_Calls()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        IGrainFactory factory = _clientHost.Services.GetRequiredService<IGrainFactory>();
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();

        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thrower = new ThrowingRecorder(invoked);
        IPositionObserver observerRef = factory.CreateObjectReference<IPositionObserver>(thrower);
        await Task.Delay(50, cts.Token);

        IVehicleGrain vehicle = client.GetGrain<IVehicleGrain>("truck-throws");
        await vehicle.Subscribe(observerRef).WaitAsync(cts.Token);

        // Triggers an ObserverInvoke whose handler throws on the client.
        await vehicle.UpdatePosition("boom").WaitAsync(cts.Token);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);

        // The connection must still be alive: a subsequent grain RPC must succeed,
        // proving the throwing observer did not fault the connection's pending map.
        await vehicle.UpdatePosition("still-alive").WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
    }

    // -------------------------------------------------------------------------
    // Test — a failed side-channel dispatch must be logged, not silently
    // swallowed, so operators can diagnose stale/throwing observers (issue #20).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Throwing_Observer_Dispatch_Is_Logged()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        IGrainFactory factory = _clientHost.Services.GetRequiredService<IGrainFactory>();
        IClusterClient client = _clientHost.Services.GetRequiredService<IClusterClient>();

        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thrower = new ThrowingRecorder(invoked);
        IPositionObserver observerRef = factory.CreateObjectReference<IPositionObserver>(thrower);
        await Task.Delay(50, cts.Token);

        IVehicleGrain vehicle = client.GetGrain<IVehicleGrain>("truck-logged");
        await vehicle.Subscribe(observerRef).WaitAsync(cts.Token);
        await vehicle.UpdatePosition("boom").WaitAsync(cts.Token);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);

        bool logged = await _logCapture.WaitForAsync(
            e => e.Level >= LogLevel.Warning
                 && e.Category == typeof(TcpGatewayConnection).FullName
                 && e.Exception is not null,
            TimeSpan.FromSeconds(5));

        Assert.True(logged, "Expected a warning log for the failed observer dispatch.");
    }

    // =========================================================================
    // Observer interface and implementations
    // =========================================================================

    public interface IPositionObserver : IGrainObserver
    {
        Task PositionUpdated(string position);
    }

    private sealed class GatedRecorder : IPositionObserver
    {
        private readonly Task _gate;
        private readonly TaskCompletionSource _invoked;

        public GatedRecorder(Task gate, TaskCompletionSource invoked)
        {
            _gate = gate;
            _invoked = invoked;
        }

        public async Task PositionUpdated(string position)
        {
            _invoked.TrySetResult();
            await _gate.ConfigureAwait(false);
        }
    }

    private sealed class ThrowingRecorder : IPositionObserver
    {
        private readonly TaskCompletionSource _invoked;

        public ThrowingRecorder(TaskCompletionSource invoked) => _invoked = invoked;

        public Task PositionUpdated(string position)
        {
            _invoked.TrySetResult();
            throw new InvalidOperationException($"boom for {position}");
        }
    }

    private sealed class PositionRecorder : IPositionObserver
    {
        private readonly TaskCompletionSource<string> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _expected;
        private int _count;
        private TaskCompletionSource? _allDone;

        public List<string> AllPositions { get; } = [];

        public PositionRecorder(int expected = 1)
        {
            _expected = expected;
            if (expected > 1) _allDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task PositionUpdated(string position)
        {
            AllPositions.Add(position);
            _first.TrySetResult(position);
            if (++_count >= _expected)
                _allDone?.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task<string?> WaitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                return await _first.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task WaitAllAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await (_allDone?.Task ?? Task.CompletedTask).WaitAsync(cts.Token);
        }
    }

    // =========================================================================
    // In-memory logger capture
    // =========================================================================

    private sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _entries);

        public async Task<bool> WaitForAsync(Func<LogEntry, bool> predicate, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            while (!cts.IsCancellationRequested)
            {
                if (_entries.Any(predicate)) return true;
                try { await Task.Delay(25, cts.Token); }
                catch (OperationCanceledException) { break; }
            }
            return _entries.Any(predicate);
        }

        public void Dispose() { }

        private sealed class CaptureLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentQueue<LogEntry> _entries;

            public CaptureLogger(string category, ConcurrentQueue<LogEntry> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _entries.Enqueue(new LogEntry(_category, logLevel, formatter(state, exception), exception));
        }
    }

    // =========================================================================
    // Observer invokable + proxy (mirrors what GrainProxyGenerator would emit)
    // =========================================================================

    private readonly struct PositionObserverProxy_PositionUpdatedInvokable : IObserverVoidInvokable
    {
        private readonly string _position;
        public PositionObserverProxy_PositionUpdatedInvokable(string position) => _position = position;
        public uint MethodId => 0u;
        public ValueTask Invoke(object target) => new(((IPositionObserver)target).PositionUpdated(_position));
        public void Serialize(ref CodecWriter writer) => writer.WriteString(_position);
        public static PositionObserverProxy_PositionUpdatedInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null)
            => new(reader.ReadString());
    }

    // Observer proxy: implements IGrainObserverProxy so the grain-ref serializer can read GrainId.
    private sealed class PositionObserverProxy
        : IPositionObserver, IGrainObserverProxy, IGrainObserverProxyActivator<PositionObserverProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;
        public PositionObserverProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }
        public GrainId GrainId => _grainId;
        public static PositionObserverProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);
        public Task PositionUpdated(string position)
        {
            Console.Error.WriteLine($"[DBG] PositionObserverProxy.PositionUpdated({position}) invoker={_invoker.GetType().Name}");
            Task t = _invoker.InvokeObserverAsync(_grainId, new PositionObserverProxy_PositionUpdatedInvokable(position)).AsTask();
            Console.Error.WriteLine($"[DBG] InvokeObserverAsync returned task Status={t.Status}");
            return t;
        }
    }

    // Client-side dispatcher: handles incoming ObserverInvoke frames for IPositionObserver.
    private sealed class PositionObserver_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly PositionObserver_TransportDispatcher Instance = new();

        public async Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory,
            CancellationToken ct = default)
        {
            switch (methodId)
            {
                case 0u:
                {
                    var reader = new CodecReader(argumentPayload);
                    var inv = PositionObserverProxy_PositionUpdatedInvokable.Deserialize(ref reader, factory);
                    await invoker.InvokeObserverAsync<PositionObserverProxy_PositionUpdatedInvokable>(
                        grainId, inv, ct).ConfigureAwait(false);
                    return ReadOnlyMemory<byte>.Empty;
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for IPositionObserver.");
        }
    }

    // =========================================================================
    // Grain interface, state, behavior
    // =========================================================================

    public interface IVehicleGrain : IGrainWithStringKey
    {
        Task Subscribe(IPositionObserver observer);
        Task UpdatePosition(string position);
    }

    private sealed class VehicleGrainState
    {
        public IPositionObserver? Observer { get; set; }
    }

    private sealed class VehicleGrainBehavior : IGrainBehavior, IVehicleGrain
    {
        private readonly IActivationMemory<VehicleGrainState> _memory;

        public VehicleGrainBehavior(IActivationMemory<VehicleGrainState> memory)
        {
            _memory = memory;
        }

        public Task Subscribe(IPositionObserver observer)
        {
            _memory.Value.Observer = observer;
            return Task.CompletedTask;
        }

        public async Task UpdatePosition(string position)
        {
            IPositionObserver? observer = _memory.Value.Observer;
            Console.Error.WriteLine($"[DBG] UpdatePosition({position}) observer={observer?.GetType().Name ?? "null"}");
            if (observer is not null)
            {
                Console.Error.WriteLine($"[DBG] calling observer.PositionUpdated({position})...");
                await observer.PositionUpdated(position).ConfigureAwait(false);
                Console.Error.WriteLine($"[DBG] observer.PositionUpdated({position}) returned");
            }
        }
    }

    // =========================================================================
    // Grain invokables + proxy
    // =========================================================================

    private readonly struct VehicleGrainProxy_SubscribeInvokable : IGrainVoidInvokable
    {
        private readonly IPositionObserver _observer;
        public VehicleGrainProxy_SubscribeInvokable(IPositionObserver observer) => _observer = observer;
        public uint MethodId => 0u;
        public ValueTask Invoke(IGrainBehavior behavior)
            => new(((IVehicleGrain)behavior).Subscribe(_observer));
        public void Serialize(ref CodecWriter writer)
        {
            var proxy = (IGrainObserverProxy)_observer;
            writer.WriteString(proxy.GrainId.Type.Value);
            writer.WriteString(proxy.GrainId.Key);
        }
    }

    private readonly struct VehicleGrainProxy_UpdatePositionInvokable : IGrainVoidInvokable
    {
        private readonly string _position;
        public VehicleGrainProxy_UpdatePositionInvokable(string position) => _position = position;
        public uint MethodId => 1u;
        public ValueTask Invoke(IGrainBehavior behavior)
            => new(((IVehicleGrain)behavior).UpdatePosition(_position));
        public void Serialize(ref CodecWriter writer) => writer.WriteString(_position);
        public static VehicleGrainProxy_UpdatePositionInvokable Deserialize(
            ref CodecReader reader, IGrainFactory? factory = null)
            => new(reader.ReadString());
    }

    private sealed class VehicleGrainProxy
        : IVehicleGrain, IGrainProxyActivator<VehicleGrainProxy>, IGrainProxy
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;
        public VehicleGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }
        public GrainId GrainId => _grainId;
        public static VehicleGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);
        public Task Subscribe(IPositionObserver observer)
            => _invoker.InvokeVoidAsync(_grainId, new VehicleGrainProxy_SubscribeInvokable(observer)).AsTask();
        public Task UpdatePosition(string position)
            => _invoker.InvokeVoidAsync(_grainId, new VehicleGrainProxy_UpdatePositionInvokable(position)).AsTask();
    }

    // =========================================================================
    // Silo-side transport dispatcher — deserialises grain calls
    // =========================================================================

    private sealed class VehicleGrain_TransportDispatcher : ITransportGrainDispatcher
    {
        public static readonly VehicleGrain_TransportDispatcher Instance = new();

        public async Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory,
            CancellationToken ct = default)
        {
            switch (methodId)
            {
                case 0u: // Subscribe(IPositionObserver observer)
                {
                    var reader = new CodecReader(argumentPayload);
                    string observerTypeName = reader.ReadString();
                    string observerKey = reader.ReadString();
                    var observerGrainId = GrainId.Create(new GrainType(observerTypeName), observerKey);
                    IPositionObserver observer = factory!.GetObserverRef<IPositionObserver>(observerGrainId);
                    await invoker.InvokeVoidAsync<VehicleGrainProxy_SubscribeInvokable>(
                        grainId, new VehicleGrainProxy_SubscribeInvokable(observer), ct).ConfigureAwait(false);
                    return ReadOnlyMemory<byte>.Empty;
                }
                case 1u: // UpdatePosition(string position)
                {
                    var reader = new CodecReader(argumentPayload);
                    var inv = VehicleGrainProxy_UpdatePositionInvokable.Deserialize(ref reader, factory);
                    await invoker.InvokeVoidAsync<VehicleGrainProxy_UpdatePositionInvokable>(
                        grainId, inv, ct).ConfigureAwait(false);
                    return ReadOnlyMemory<byte>.Empty;
                }
            }
            throw new InvalidOperationException($"Unknown method {methodId} for VehicleGrain.");
        }
    }
}
