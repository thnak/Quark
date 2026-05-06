using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Tests.Unit.Integration;

public sealed class GrainCallFixture : IAsyncDisposable
{
    private readonly GrainActivationTable _activationTable;
    private readonly ServiceProvider _serviceProvider;

    public GrainCallFixture()
    {
        var services = new ServiceCollection();

        // Logging (NullLogger so tests don't need log infrastructure)
        services.AddLogging();

        // SiloRuntimeOptions
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "integration";
            o.SiloName = "silo0";
        });

        // Runtime core services (minus the hosted service)
        services.AddSingleton<LifecycleSubject>();
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<IGrainActivator, DefaultGrainActivator>();
        services.AddSingleton<GrainActivationTable>();
        services.AddSingleton<GrainMethodInvokerRegistry>();
        services.AddSingleton<IGrainMethodInvokerRegistry>(sp =>
            sp.GetRequiredService<GrainMethodInvokerRegistry>());

        // Register grain
        services.AddTransient<CounterGrain>();

        // Register method invoker
        services.AddSingleton<CounterGrainMethodInvoker>();

        // Client-side registries
        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        _serviceProvider = services.BuildServiceProvider();

        // Apply deferred registrations (normally done by hosted services)
        GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("CounterGrain"), typeof(CounterGrain));

        GrainMethodInvokerRegistry invokerRegistry = _serviceProvider.GetRequiredService<GrainMethodInvokerRegistry>();
        invokerRegistry.Register(typeof(CounterGrain),
            _serviceProvider.GetRequiredService<CounterGrainMethodInvoker>());

        GrainProxyFactoryRegistry proxyRegistry = _serviceProvider.GetRequiredService<GrainProxyFactoryRegistry>();
        GrainInterfaceTypeRegistry interfaceRegistry =
            _serviceProvider.GetRequiredService<GrainInterfaceTypeRegistry>();
        interfaceRegistry.Register(typeof(ICounterGrain), new GrainType("CounterGrain"));
        proxyRegistry.Register<ICounterGrain, CounterGrainProxy>((grainId, invoker) =>
            new CounterGrainProxy(grainId, invoker));

        // Construct LocalGrainCallInvoker manually (avoid hosted service dependency)
        _activationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
        IGrainActivator activator = _serviceProvider.GetRequiredService<IGrainActivator>();
        IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
        IGrainMethodInvokerRegistry methodInvokerReg =
            _serviceProvider.GetRequiredService<IGrainMethodInvokerRegistry>();
        IOptions<SiloRuntimeOptions> siloOptions = _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();
        NullLogger<LocalGrainCallInvoker> logger = NullLogger<LocalGrainCallInvoker>.Instance;
        NullLogger<GrainActivation> logger2 = NullLogger<GrainActivation>.Instance;

        // Build a LocalGrainFactory so it's available for grain inter-calls
        var dummyInvoker = new DeferredLocalGrainCallInvoker();
        var localFactory = new LocalGrainFactory(proxyRegistry, interfaceRegistry, dummyInvoker);

        var callInvoker = new LocalGrainCallInvoker(
            _activationTable, activator, typeRegistry, directory,
            methodInvokerReg, localFactory, _serviceProvider, siloOptions, logger,
            logger2);

        // Wire back the real invoker
        dummyInvoker.SetInvoker(callInvoker);

        // Build the client
        var factory = new LocalGrainFactory(proxyRegistry, interfaceRegistry, callInvoker);
        Client = new LocalClusterClient(factory);
    }

    public IClusterClient Client { get; }

    public async ValueTask DisposeAsync()
    {
        await _activationTable.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }

    // Wrapper that resolves the invoker lazily to break the circular construction
    private sealed class DeferredLocalGrainCallInvoker : IGrainCallInvoker
    {
        private IGrainCallInvoker? _inner;

        public Task<object?> InvokeAsync(GrainId id, uint method, object?[]? args = null,
            CancellationToken ct = default)
        {
            return _inner!.InvokeAsync(id, method, args, ct);
        }

        public Task<TResult> InvokeAsync<TResult>(GrainId id, uint method, object?[]? args = null,
            CancellationToken ct = default)
        {
            return _inner!.InvokeAsync<TResult>(id, method, args, ct);
        }

        public Task InvokeVoidAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
        {
            return _inner!.InvokeVoidAsync(id, method, args, ct);
        }

        public void SetInvoker(IGrainCallInvoker invoker)
        {
            _inner = invoker;
        }
    }
}
