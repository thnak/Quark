using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Tests.Unit.Integration;

public sealed class EagerMemoryFixture : IAsyncDisposable
{
    private readonly GrainActivationTable _activationTable;
    private readonly ServiceProvider _serviceProvider;

    public EagerMemoryFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "eager-memory";
            o.SiloName = "silo0";
        });

        services.AddSingleton<LifecycleSubject>();
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<GrainActivationTable>();

        services.AddScoped<ActivationShellAccessor>();
        services.AddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
        services.AddScoped<CallContext>();
        services.AddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
        services.AddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
        services.AddScoped<IBehaviorResolver, BehaviorResolver>();

        // The scoped service injected via the factory's IServiceProvider.
        services.AddScoped<EagerScopedService>();

        services.AddEagerActivationMemory<EagerResource>();
        services.AddTransient<EagerResourceBehavior>();

        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        LocalGrainFactory? grainFactoryRef = null;
        services.AddSingleton<IGrainFactory>(_ =>
            grainFactoryRef ?? throw new InvalidOperationException("Not yet wired."));

        _serviceProvider = services.BuildServiceProvider();

        GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("EagerResourceGrain"), typeof(EagerResourceBehavior));

        GrainProxyFactoryRegistry proxyRegistry = _serviceProvider.GetRequiredService<GrainProxyFactoryRegistry>();
        GrainInterfaceTypeRegistry interfaceRegistry = _serviceProvider.GetRequiredService<GrainInterfaceTypeRegistry>();
        interfaceRegistry.Register(typeof(IEagerResourceGrain), new GrainType("EagerResourceGrain"));
        proxyRegistry.Register<IEagerResourceGrain, EagerResourceGrainProxy>((grainId, invoker) =>
            new EagerResourceGrainProxy(grainId, invoker));

        _activationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
        IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
        IOptions<SiloRuntimeOptions> siloOptions = _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();

        var callInvoker = new LocalGrainCallInvoker(
            _activationTable, typeRegistry, directory,
            _serviceProvider, siloOptions,
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance);

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
