using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;

namespace Quark.Tests.Unit.FailureSemantics;

/// <summary>
///     Hand-wired DI harness for pinning behavior-call/activation/deactivation/timer failure
///     semantics (issue #130) — the same direct <see cref="GrainActivationTable" />+<see cref="LocalGrainCallInvoker" />
///     wiring used by <c>Quark.Tests.Fault</c>, without the storage-fault-injection machinery those
///     tests need but these don't.
/// </summary>
public sealed class FailureSemanticsFixture : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public FailureSemanticsFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "failure-semantics";
            o.SiloName = "silo0";
        });

        services.AddSingleton<LifecycleSubject>();
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<GrainActivationTable>();
        services.AddSingleton<GrainBehaviorFactoryRegistry>();

        services.AddScoped<ActivationShellAccessor>();
        services.AddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
        services.AddScoped<CallContext>();
        services.AddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
        services.AddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
        services.AddScoped<IBehaviorResolver, BehaviorResolver>();

        // The fake clock is registered so GrainTimerCreationOptions with no explicit TimeProvider
        // resolves it automatically (see GrainActivation.RegisterTimer) — tests advance it instead
        // of sleeping on the real one.
        Clock = new FakeTimeProvider();
        services.AddSingleton<TimeProvider>(Clock);

        ActivationGate = new ActivationGate();
        services.AddSingleton(ActivationGate);
        DeactivationTracker = new DeactivationTracker();
        services.AddSingleton(DeactivationTracker);

        services.AddScoped<IActivationMemory<FailureState>>(sp =>
            new ActivationMemoryAccessor<FailureState>(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<FailureState>()));
        services.AddTransient<FailureBehavior>();

        services.AddTransient<FlakyActivationBehavior>();

        services.AddScoped<IActivationMemory<TimerLifecycleState>>(sp =>
            new ActivationMemoryAccessor<TimerLifecycleState>(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<TimerLifecycleState>()));
        services.AddManagedActivationMemory<TrackedResource>();
        services.AddTransient<TimerLifecycleBehavior>();

        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        LocalGrainFactory? grainFactoryRef = null;
        services.AddSingleton<IGrainFactory>(_ =>
            grainFactoryRef ?? throw new InvalidOperationException("Not yet wired."));

        _serviceProvider = services.BuildServiceProvider();

        GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("FailureGrain"), typeof(FailureBehavior));
        typeRegistry.Register(new GrainType("FlakyActivationGrain"), typeof(FlakyActivationBehavior));
        typeRegistry.Register(new GrainType("TimerLifecycleGrain"), typeof(TimerLifecycleBehavior));

        GrainProxyFactoryRegistry proxyRegistry = _serviceProvider.GetRequiredService<GrainProxyFactoryRegistry>();
        GrainInterfaceTypeRegistry interfaceRegistry = _serviceProvider.GetRequiredService<GrainInterfaceTypeRegistry>();

        interfaceRegistry.Register(typeof(IFailureGrain), new GrainType("FailureGrain"));
        proxyRegistry.Register<IFailureGrain, FailureGrainProxy>(FailureGrainProxy.Create);

        interfaceRegistry.Register(typeof(IFlakyActivationGrain), new GrainType("FlakyActivationGrain"));
        proxyRegistry.Register<IFlakyActivationGrain, FlakyActivationGrainProxy>(FlakyActivationGrainProxy.Create);

        interfaceRegistry.Register(typeof(ITimerLifecycleGrain), new GrainType("TimerLifecycleGrain"));
        proxyRegistry.Register<ITimerLifecycleGrain, TimerLifecycleGrainProxy>(TimerLifecycleGrainProxy.Create);

        ActivationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
        IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
        IOptions<SiloRuntimeOptions> siloOptions = _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();

        var callInvoker = new LocalGrainCallInvoker(
            ActivationTable, typeRegistry, directory,
            _serviceProvider, siloOptions,
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance);

        grainFactoryRef = new LocalGrainFactory(proxyRegistry, interfaceRegistry, callInvoker);
        Client = new LocalClusterClient(grainFactoryRef);
    }

    public IClusterClient Client { get; }
    public GrainActivationTable ActivationTable { get; }
    public FakeTimeProvider Clock { get; }
    public ActivationGate ActivationGate { get; }
    public DeactivationTracker DeactivationTracker { get; }

    public async ValueTask DisposeAsync()
    {
        await ActivationTable.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }
}
