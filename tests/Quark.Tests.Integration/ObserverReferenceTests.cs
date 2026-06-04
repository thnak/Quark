using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization;
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
    // Observer proxy (hand-written; mirrors what code generator emits)
    // -----------------------------------------------------------------------

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
            => _invoker.InvokeVoidAsync(_grainId, 0u, new object?[] { message });
    }

    private sealed class EventObserverMethodInvoker : IObserverMethodInvoker
    {
        public ValueTask<object?> Invoke(object target, uint methodId, object?[]? arguments)
        {
            var observer = (IEventObserver)target;
            return methodId switch
            {
                0u => new ValueTask<object?>(observer.OnEventAsync((string)arguments![0]!)
                    .ContinueWith(_ => (object?)null)),
                _ => throw new NotSupportedException($"Unknown method id {methodId}")
            };
        }
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
    // Hand-written proxy + invoker
    // -----------------------------------------------------------------------

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
            => _invoker.InvokeAsync<int>(_grainId, 0u, null);

        public Task PublishAsync(string message, IEventObserver observer)
            => _invoker.InvokeVoidAsync(_grainId, 1u, new object?[] { message, observer });

        public Task<int> GetCountAsync()
            => _invoker.InvokeAsync<int>(_grainId, 2u, null);
    }

    private sealed class EventSourceGrainMethodInvoker : IGrainMethodInvoker
    {
        public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
        {
            var typed = (EventSourceGrain)grain;
            return methodId switch
            {
                0u => await typed.IncrementViaSelfRefAsync(),
                1u => await typed.PublishAsync((string)arguments![0]!, (IEventObserver)arguments[1]!)
                    .ContinueWith(_ => (object?)null),
                2u => await typed.GetCountAsync(),
                _ => throw new NotSupportedException($"Unknown method id {methodId}")
            };
        }
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
            services.AddSingleton<EventSourceGrainMethodInvoker>();
            services.AddSingleton<EventObserverMethodInvoker>();

            // Client registries
            services.AddSingleton<GrainProxyFactoryRegistry>();
            services.AddSingleton<GrainInterfaceTypeRegistry>();
            services.AddSingleton<ObserverProxyFactoryRegistry>();

            _serviceProvider = services.BuildServiceProvider();

            // Manual deferred registrations
            var typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
            typeRegistry.Register(new GrainType("EventSourceGrain"), typeof(EventSourceGrain));

            var invokerRegistry = _serviceProvider.GetRequiredService<GrainMethodInvokerRegistry>();
            invokerRegistry.Register(typeof(EventSourceGrain),
                _serviceProvider.GetRequiredService<EventSourceGrainMethodInvoker>());

            var observerInvokerRegistry = _serviceProvider.GetRequiredService<ObserverMethodInvokerRegistry>();
            observerInvokerRegistry.Register(typeof(IEventObserver),
                _serviceProvider.GetRequiredService<EventObserverMethodInvoker>());

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
                observerProxyRegistry, observerRegistry,
                observerInvokerRegistry);

            var realInvoker = new LocalGrainCallInvoker(
                _activationTable,
                _serviceProvider.GetRequiredService<IGrainActivator>(),
                typeRegistry,
                _serviceProvider.GetRequiredService<IGrainDirectory>(),
                _serviceProvider.GetRequiredService<IGrainMethodInvokerRegistry>(),
                localFactory,
                _serviceProvider,
                _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
                NullLogger<LocalGrainCallInvoker>.Instance,
                NullLogger<GrainActivation>.Instance,
                observerRegistry);

            deferredInvoker.SetInvoker(realInvoker);

            Factory = new LocalGrainFactory(proxyRegistry, interfaceRegistry, realInvoker,
                observerProxyRegistry, observerRegistry, observerInvokerRegistry);
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
            public Task<object?> InvokeAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default) => _inner!.InvokeAsync(id, method, args, ct);
            public Task<TResult> InvokeAsync<TResult>(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default) => _inner!.InvokeAsync<TResult>(id, method, args, ct);
            public Task InvokeVoidAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default) => _inner!.InvokeVoidAsync(id, method, args, ct);
        }
    }
}
