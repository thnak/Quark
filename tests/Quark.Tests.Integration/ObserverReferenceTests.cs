using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Integration;

/// <summary>
///     Verifies F-05:
///     <list type="bullet">
///         <item><c>AsReference&lt;T&gt;()</c> — grain gets proxy for its own identity</item>
///         <item><c>CreateObjectReference&lt;T&gt;()</c> — wraps a local CLR object as an observer,
///             calls route to the local object via GrainId-based dispatch</item>
///     </list>
/// </summary>
public sealed class ObserverReferenceTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new ObserverFixture();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _fixture.DisposeAsync().AsTask();

    [Fact]
    public async Task AsReference_Returns_Proxy_For_Own_GrainId()
    {
        IEventSourceGrain grain = _fixture.Client.GetGrain<IEventSourceGrain>("self-ref-test");

        // Grain calls IncrementViaSelfRefAsync which internally uses AsReference<IEventSourceGrain>()
        // to get a proxy to itself, then calls GetCountAsync() through that proxy.
        int result = await grain.IncrementViaSelfRefAsync();
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task CreateObjectReference_Routes_Calls_To_Local_Object()
    {
        var recorder = new EventRecorder();
        IEventObserver observerRef = _fixture.Factory.CreateObjectReference<IEventObserver>(recorder);

        IEventSourceGrain grain = _fixture.Client.GetGrain<IEventSourceGrain>("observer-test");
        await grain.PublishAsync("hello", observerRef);
        await grain.PublishAsync("world", observerRef);

        Assert.Equal(["hello", "world"], recorder.Events);
    }

    // -----------------------------------------------------------------------
    // Observer interface and local implementation
    // -----------------------------------------------------------------------

    public interface IEventObserver : IGrainObserver
    {
        Task OnEventAsync(string message);
    }

    private sealed class EventRecorder : IEventObserver
    {
        public List<string> Events { get; } = [];
        public Task OnEventAsync(string message) { Events.Add(message); return Task.CompletedTask; }
    }

    // -----------------------------------------------------------------------
    // Observer invokable + proxy (hand-written; mirrors what code generator emits)
    // -----------------------------------------------------------------------

    private readonly struct EventObserverProxy_OnEventAsyncInvokable : IObserverVoidInvokable
    {
        private readonly string _message;
        public EventObserverProxy_OnEventAsyncInvokable(string message) => _message = message;
        public uint MethodId => 0u;
        public ValueTask Invoke(object target) => new(((IEventObserver)target).OnEventAsync(_message));
        public void Serialize(ref CodecWriter writer) { }
    }

    private sealed class EventObserverProxy
        : IEventObserver, IGrainObserverProxyActivator<EventObserverProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public EventObserverProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public static EventObserverProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task OnEventAsync(string message)
            => _invoker.InvokeObserverAsync(_grainId, new EventObserverProxy_OnEventAsyncInvokable(message));
    }

    // -----------------------------------------------------------------------
    // Grain interface + implementation
    // -----------------------------------------------------------------------

    public interface IEventSourceGrain : IGrainWithStringKey
    {
        Task<int> IncrementViaSelfRefAsync();
        Task PublishAsync(string message, IEventObserver observer);
        Task<int> GetCountAsync();
    }

    [Reentrant]
    private sealed class EventSourceGrain : Grain, IEventSourceGrain
    {
        private int _count;

        public async Task<int> IncrementViaSelfRefAsync()
        {
            IEventSourceGrain self = AsReference<IEventSourceGrain>();
            _count++;
            // Verify the self-ref proxy routes back to this grain
            return await self.GetCountAsync();
        }

        public async Task PublishAsync(string message, IEventObserver observer)
        {
            _count++;
            await observer.OnEventAsync(message);
        }

        public Task<int> GetCountAsync() => Task.FromResult(_count);
    }

    // -----------------------------------------------------------------------
    // Hand-written invokables + proxy
    // -----------------------------------------------------------------------

    private readonly struct EventSourceGrainProxy_IncrementViaSelfRefAsyncInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 0u;
        public ValueTask<int> Invoke(Grain grain) => new(((IEventSourceGrain)grain).IncrementViaSelfRefAsync());
        public void Serialize(ref CodecWriter writer) { }
        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private readonly struct EventSourceGrainProxy_PublishAsyncInvokable : IGrainVoidInvokable
    {
        private readonly string _message;
        private readonly IEventObserver _observer;
        public EventSourceGrainProxy_PublishAsyncInvokable(string message, IEventObserver observer)
        {
            _message = message;
            _observer = observer;
        }
        public uint MethodId => 1u;
        public ValueTask Invoke(Grain grain) => new(((IEventSourceGrain)grain).PublishAsync(_message, _observer));
        public void Serialize(ref CodecWriter writer) { }
    }

    private readonly struct EventSourceGrainProxy_GetCountAsyncInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 2u;
        public ValueTask<int> Invoke(Grain grain) => new(((IEventSourceGrain)grain).GetCountAsync());
        public void Serialize(ref CodecWriter writer) { }
        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private sealed class EventSourceGrainProxy
        : IEventSourceGrain, IGrainProxyActivator<EventSourceGrainProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public EventSourceGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public static EventSourceGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task<int> IncrementViaSelfRefAsync()
            => _invoker.InvokeAsync<EventSourceGrainProxy_IncrementViaSelfRefAsyncInvokable, int>(
                _grainId, new EventSourceGrainProxy_IncrementViaSelfRefAsyncInvokable());

        public Task PublishAsync(string message, IEventObserver observer)
            => _invoker.InvokeVoidAsync(_grainId, new EventSourceGrainProxy_PublishAsyncInvokable(message, observer));

        public Task<int> GetCountAsync()
            => _invoker.InvokeAsync<EventSourceGrainProxy_GetCountAsyncInvokable, int>(
                _grainId, new EventSourceGrainProxy_GetCountAsyncInvokable());
    }

    private sealed class EventSourceGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(EventSourceGrain);
        public Grain Create(GrainId grainId, IServiceProvider services) => new EventSourceGrain();
    }

    // -----------------------------------------------------------------------
    // Fixture
    // -----------------------------------------------------------------------

    private sealed class ObserverFixture : IAsyncDisposable
    {
        private readonly GrainActivationTable _activationTable;
        private readonly ServiceProvider _serviceProvider;

        public ObserverFixture()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddQuarkSerialization();
            services.Configure<SiloRuntimeOptions>(o =>
            {
                o.ClusterId = "test";
                o.ServiceId = "integration";
                o.SiloName = "silo0";
            });

            services.AddQuarkRuntime();
            services.AddSingleton<IGrainActivatorFactory>(new EventSourceGrainActivatorFactory());

            // Client registries
            services.AddSingleton<GrainProxyFactoryRegistry>();
            services.AddSingleton<GrainInterfaceTypeRegistry>();
            services.AddSingleton<ObserverProxyFactoryRegistry>();

            _serviceProvider = services.BuildServiceProvider();

            // Manual deferred registrations
            var typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
            typeRegistry.Register(new GrainType("EventSourceGrain"), typeof(EventSourceGrain));

            var proxyRegistry = _serviceProvider.GetRequiredService<GrainProxyFactoryRegistry>();
            var interfaceRegistry = _serviceProvider.GetRequiredService<GrainInterfaceTypeRegistry>();
            interfaceRegistry.Register(typeof(IEventSourceGrain), new GrainType("EventSourceGrain"));
            proxyRegistry.Register<IEventSourceGrain, EventSourceGrainProxy>(
                (id, inv) => new EventSourceGrainProxy(id, inv));

            var observerProxyRegistry = _serviceProvider.GetRequiredService<ObserverProxyFactoryRegistry>();
            observerProxyRegistry.Register<IEventObserver, EventObserverProxy>(
                (id, inv) => new EventObserverProxy(id, inv));

            _activationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();

            // Build invoker with observer registry support
            var observerRegistry = _serviceProvider.GetRequiredService<ObserverRegistry>();

            var deferredInvoker = new DeferredGrainCallInvoker();
            var localFactory = new LocalGrainFactory(
                proxyRegistry, interfaceRegistry, deferredInvoker,
                observerProxyRegistry, observerRegistry);

            var realInvoker = new LocalGrainCallInvoker(
                _activationTable,
                _serviceProvider.GetRequiredService<IGrainActivator>(),
                typeRegistry,
                _serviceProvider.GetRequiredService<IGrainDirectory>(),
                _serviceProvider,
                _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
                NullLogger<LocalGrainCallInvoker>.Instance,
                NullLogger<GrainActivation>.Instance,
                observerRegistry,
                grainFactory: localFactory);

            deferredInvoker.SetInvoker(realInvoker);

            Factory = new LocalGrainFactory(proxyRegistry, interfaceRegistry, realInvoker,
                observerProxyRegistry, observerRegistry);
            Client = new LocalGrainFactory(proxyRegistry, interfaceRegistry, realInvoker);
        }

        public LocalGrainFactory Factory { get; }
        public LocalGrainFactory Client { get; }

        public async ValueTask DisposeAsync()
        {
            await _activationTable.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }

        private sealed class DeferredGrainCallInvoker : IGrainCallInvoker
        {
            private IGrainCallInvoker? _inner;
            public void SetInvoker(IGrainCallInvoker invoker) => _inner = invoker;
            public Task<TResult> InvokeAsync<TInvokable, TResult>(GrainId id, TInvokable invokable, CancellationToken ct = default) where TInvokable : struct, IGrainInvokable<TResult> => _inner!.InvokeAsync<TInvokable, TResult>(id, invokable, ct);
            public Task InvokeVoidAsync<TInvokable>(GrainId id, TInvokable invokable, CancellationToken ct = default) where TInvokable : struct, IGrainVoidInvokable => _inner!.InvokeVoidAsync<TInvokable>(id, invokable, ct);
            public Task InvokeObserverAsync<TInvokable>(GrainId id, TInvokable invokable, CancellationToken ct = default) where TInvokable : struct, IObserverVoidInvokable => _inner!.InvokeObserverAsync<TInvokable>(id, invokable, ct);
        }
    }
}
