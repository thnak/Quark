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

        // Register grain activator factory (no-reflection, AOT-safe)
        services.AddSingleton<IGrainActivatorFactory>(new CounterGrainActivatorFactory());

        // Client-side registries
        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        // Register IGrainFactory as a deferred singleton so LocalGrainCallInvoker can resolve it
        // lazily without a circular dependency at construction time.
        LocalGrainFactory? grainFactoryRef = null;
        services.AddSingleton<IGrainFactory>(_ =>
            grainFactoryRef ?? throw new InvalidOperationException("LocalGrainFactory not yet wired."));

        _serviceProvider = services.BuildServiceProvider();

        // Apply deferred registrations (normally done by hosted services)
        GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("CounterGrain"), typeof(CounterGrain));

        GrainProxyFactoryRegistry proxyRegistry = _serviceProvider.GetRequiredService<GrainProxyFactoryRegistry>();
        GrainInterfaceTypeRegistry interfaceRegistry =
            _serviceProvider.GetRequiredService<GrainInterfaceTypeRegistry>();
        interfaceRegistry.Register(typeof(ICounterGrain), new GrainType("CounterGrain"));
        proxyRegistry.Register<ICounterGrain, CounterGrainProxy>((grainId, invoker) =>
            new CounterGrainProxy(grainId, invoker));

        // Construct LocalGrainCallInvoker manually (avoid hosted service dependency).
        // IGrainFactory is resolved lazily from _serviceProvider on first grain activation.
        _activationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
        IGrainActivator activator = _serviceProvider.GetRequiredService<IGrainActivator>();
        IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
        IOptions<SiloRuntimeOptions> siloOptions = _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();
        NullLogger<LocalGrainCallInvoker> logger = NullLogger<LocalGrainCallInvoker>.Instance;
        NullLogger<GrainActivation> logger2 = NullLogger<GrainActivation>.Instance;

        var callInvoker = new LocalGrainCallInvoker(
            _activationTable, activator, typeRegistry, directory,
            _serviceProvider, siloOptions, logger, logger2);

        // Wire the deferred factory reference — resolves when first grain is activated.
        grainFactoryRef = new LocalGrainFactory(proxyRegistry, interfaceRegistry, callInvoker);
        Client = new LocalClusterClient(grainFactoryRef);
    }

    public IClusterClient Client { get; }
    public GrainActivationTable ActivationTable => _activationTable;

    public async ValueTask DisposeAsync()
    {
        await _activationTable.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }

}
