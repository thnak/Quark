using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Tests.Fault.Fakes;
using Quark.Tests.Fault.Grains;

namespace Quark.Tests.Fault;

public sealed class FaultFixture : IAsyncDisposable
{
    private readonly ServiceProvider _sp;
    private readonly GrainActivationTable _activationTable;

    public FaultFixture(Action<FaultScenarioHolder>? configure = null)
    {
        ScenarioHolder = new FaultScenarioHolder();
        configure?.Invoke(ScenarioHolder);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkSerialization();

        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "fault-test";
            o.ServiceId = "fault";
            o.SiloName = "silo0";
        });

        // Fault-injecting storage — registered per concrete state type
        var workerStorage = new FaultInjectingStorage<WorkerState>(ScenarioHolder.WorkerStorage);
        var orchestratorStorage = new FaultInjectingStorage<OrchestratorState>(ScenarioHolder.OrchestratorStorage);
        services.AddSingleton<IStorage<WorkerState>>(workerStorage);
        services.AddSingleton<IStorage<OrchestratorState>>(orchestratorStorage);

        // Core runtime — registry, directory, activation table
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<GrainActivationTable>();
        services.AddSingleton<GrainMethodInvokerRegistry>();
        services.AddSingleton<IGrainMethodInvokerRegistry>(sp => sp.GetRequiredService<GrainMethodInvokerRegistry>());

        // Grain activator factories
        services.AddSingleton<IGrainActivatorFactory>(new WorkerGrainActivatorFactory());
        services.AddSingleton<IGrainActivatorFactory>(new OrderOrchestratorGrainActivatorFactory());

        // Fault-injecting activator wraps DefaultGrainActivator
        services.AddSingleton<DefaultGrainActivator>();
        services.AddSingleton<IGrainActivator>(sp =>
            new FaultInjectingGrainActivator(
                sp.GetRequiredService<DefaultGrainActivator>(),
                sp.GetRequiredService<IGrainTypeRegistry>(),
                ScenarioHolder.Activations));

        // Client-side registries
        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        _sp = services.BuildServiceProvider();

        // Deferred registrations (normally done by hosted services)
        var typeRegistry = _sp.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("WorkerGrain"), typeof(WorkerGrain));
        typeRegistry.Register(new GrainType("OrderOrchestratorGrain"), typeof(OrderOrchestratorGrain));

        var proxyRegistry = _sp.GetRequiredService<GrainProxyFactoryRegistry>();
        var interfaceRegistry = _sp.GetRequiredService<GrainInterfaceTypeRegistry>();

        interfaceRegistry.Register(typeof(IWorkerGrain), new GrainType("WorkerGrain"));
        interfaceRegistry.Register(typeof(IOrderOrchestratorGrain), new GrainType("OrderOrchestratorGrain"));
        proxyRegistry.Register<IWorkerGrain, WorkerGrainProxy>((id, inv) => new WorkerGrainProxy(id, inv));
        proxyRegistry.Register<IOrderOrchestratorGrain, OrderOrchestratorGrainProxy>((id, inv) => new OrderOrchestratorGrainProxy(id, inv));

        // Break circular dep: LocalGrainFactory ↔ LocalGrainCallInvoker
        var deferredInvoker = new DeferredGrainCallInvoker();
        var localFactory = new LocalGrainFactory(proxyRegistry, interfaceRegistry, deferredInvoker);

        _activationTable = _sp.GetRequiredService<GrainActivationTable>();
        var realInvoker = new LocalGrainCallInvoker(
            _activationTable,
            _sp.GetRequiredService<IGrainActivator>(),
            typeRegistry,
            _sp.GetRequiredService<IGrainDirectory>(),
            _sp.GetRequiredService<IGrainMethodInvokerRegistry>(),
            _sp,
            _sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance,
            grainFactory: localFactory);

        // Fault-injecting call invoker wraps the real one
        IGrainCallInvoker effectiveInvoker = new FaultInjectingGrainCallInvoker(realInvoker, ScenarioHolder.Calls);
        deferredInvoker.SetInvoker(effectiveInvoker);

        Client = new LocalClusterClient(new LocalGrainFactory(proxyRegistry, interfaceRegistry, effectiveInvoker));
    }

    public IClusterClient Client { get; }
    public FaultScenarioHolder ScenarioHolder { get; }

    public async ValueTask DisposeAsync()
    {
        await _activationTable.DisposeAsync();
        await _sp.DisposeAsync();
    }

    private sealed class DeferredGrainCallInvoker : IGrainCallInvoker
    {
        private IGrainCallInvoker? _inner;

        public void SetInvoker(IGrainCallInvoker invoker) => _inner = invoker;

        public Task<object?> InvokeAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeAsync(id, method, args, ct);

        public Task<TResult> InvokeAsync<TResult>(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeAsync<TResult>(id, method, args, ct);

        public Task InvokeVoidAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeVoidAsync(id, method, args, ct);

        public Task<TResult> InvokeAsync<TInvokable, TResult>(GrainId id, TInvokable invokable, CancellationToken ct = default)
            where TInvokable : struct, IGrainInvokable<TResult>
            => _inner!.InvokeAsync<TInvokable, TResult>(id, invokable, ct);

        public Task InvokeVoidAsync<TInvokable>(GrainId id, TInvokable invokable, CancellationToken ct = default)
            where TInvokable : struct, IGrainVoidInvokable
            => _inner!.InvokeVoidAsync<TInvokable>(id, invokable, ct);

        public Task InvokeObserverAsync<TInvokable>(GrainId id, TInvokable invokable, CancellationToken ct = default)
            where TInvokable : struct, IObserverVoidInvokable
            => _inner!.InvokeObserverAsync<TInvokable>(id, invokable, ct);
    }
}
