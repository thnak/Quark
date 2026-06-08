using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
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

        // Register fault-injecting behavior resolver BEFORE AddQuarkRuntime so TryAdd skips it
        services.AddScoped<IBehaviorResolver>(sp =>
            new FaultInjectingBehaviorResolver(
                sp,
                sp.GetRequiredService<IGrainTypeRegistry>(),
                ScenarioHolder.Activations));

        // Core runtime (scoped shell/call-context services, activation table, etc.)
        services.AddQuarkRuntime();

        // Per-call persistent memory for each behavior state type
        services.AddScoped<IPersistentActivationMemory<WorkerState>>(sp =>
            new PersistentActivationMemoryAccessor<WorkerState>(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<WorkerState>(),
                sp.GetRequiredService<IStorage<WorkerState>>(),
                sp.GetRequiredService<ICallContext>(),
                StorageOptions.DefaultStateName));

        services.AddScoped<IPersistentActivationMemory<OrchestratorState>>(sp =>
            new PersistentActivationMemoryAccessor<OrchestratorState>(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<OrchestratorState>(),
                sp.GetRequiredService<IStorage<OrchestratorState>>(),
                sp.GetRequiredService<ICallContext>(),
                StorageOptions.DefaultStateName));

        // Behavior types — transient, created per call via IBehaviorResolver
        services.AddTransient<WorkerBehavior>();
        services.AddTransient<OrderOrchestratorBehavior>();

        // Client-side registries
        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        // IGrainFactory lazy registration — breaks the LocalGrainCallInvoker ↔ IGrainFactory cycle.
        // The factory lambda captures _faultInvoker so external Client can share the same instance.
        FaultInjectingGrainCallInvoker? faultInvokerRef = null;
        services.AddSingleton<IGrainFactory>(sp =>
        {
            faultInvokerRef ??= new FaultInjectingGrainCallInvoker(
                sp.GetRequiredService<LocalGrainCallInvoker>(),
                ScenarioHolder.Calls);
            return new LocalGrainFactory(
                sp.GetRequiredService<GrainProxyFactoryRegistry>(),
                sp.GetRequiredService<GrainInterfaceTypeRegistry>(),
                faultInvokerRef);
        });

        _sp = services.BuildServiceProvider();

        // Apply deferred type registry registrations manually (SiloHostedService not running)
        var typeRegistry = _sp.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("WorkerGrain"), typeof(WorkerBehavior));
        typeRegistry.Register(new GrainType("OrderOrchestratorGrain"), typeof(OrderOrchestratorBehavior));

        var proxyRegistry = _sp.GetRequiredService<GrainProxyFactoryRegistry>();
        var interfaceRegistry = _sp.GetRequiredService<GrainInterfaceTypeRegistry>();

        interfaceRegistry.Register(typeof(IWorkerGrain), new GrainType("WorkerGrain"));
        interfaceRegistry.Register(typeof(IOrderOrchestratorGrain), new GrainType("OrderOrchestratorGrain"));
        proxyRegistry.Register<IWorkerGrain, WorkerGrainProxy>((id, inv) => new WorkerGrainProxy(id, inv));
        proxyRegistry.Register<IOrderOrchestratorGrain, OrderOrchestratorGrainProxy>((id, inv) => new OrderOrchestratorGrainProxy(id, inv));

        _activationTable = _sp.GetRequiredService<GrainActivationTable>();

        // Force-resolve IGrainFactory to initialize faultInvokerRef
        _ = _sp.GetRequiredService<IGrainFactory>();

        Client = new LocalClusterClient(new LocalGrainFactory(proxyRegistry, interfaceRegistry, faultInvokerRef!));
    }

    public IClusterClient Client { get; }
    public FaultScenarioHolder ScenarioHolder { get; }

    public async ValueTask DisposeAsync()
    {
        await _activationTable.DisposeAsync();
        await _sp.DisposeAsync();
    }
}
