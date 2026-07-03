using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Hand-wired DI harness for pinning mailbox/scheduling/reentrancy failure semantics
///     (issue #131) — same direct <see cref="GrainActivationTable" />+<see cref="LocalGrainCallInvoker" />
///     wiring as <c>Quark.Tests.Unit.FailureSemantics.FailureSemanticsFixture</c>, with a
///     configurable mailbox capacity/full-mode for the bounded-mailbox tests.
/// </summary>
public sealed class SchedulingSemanticsFixture : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public SchedulingSemanticsFixture(int mailboxCapacity = 0, MailboxFullMode mailboxFullMode = MailboxFullMode.Wait)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "scheduling-semantics";
            o.SiloName = "silo0";
            o.MailboxCapacity = mailboxCapacity;
            o.MailboxFullMode = mailboxFullMode;
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

        Clock = new FakeTimeProvider();
        services.AddSingleton<TimeProvider>(Clock);

        Gate = new Gate();
        services.AddSingleton(Gate);
        EntryLog = new EntryLog();
        services.AddSingleton(EntryLog);

        services.AddScoped<IActivationMemory<SchedulingState>>(sp =>
            new ActivationMemoryAccessor<SchedulingState>(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<SchedulingState>()));
        services.AddTransient<SchedulingBehavior>();

        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        LocalGrainFactory? grainFactoryRef = null;
        services.AddSingleton<IGrainFactory>(_ =>
            grainFactoryRef ?? throw new InvalidOperationException("Not yet wired."));

        _serviceProvider = services.BuildServiceProvider();

        GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("SchedulingGrain"), typeof(SchedulingBehavior));

        GrainProxyFactoryRegistry proxyRegistry = _serviceProvider.GetRequiredService<GrainProxyFactoryRegistry>();
        GrainInterfaceTypeRegistry interfaceRegistry = _serviceProvider.GetRequiredService<GrainInterfaceTypeRegistry>();
        interfaceRegistry.Register(typeof(ISchedulingGrain), new GrainType("SchedulingGrain"));
        proxyRegistry.Register<ISchedulingGrain, SchedulingGrainProxy>(SchedulingGrainProxy.Create);

        ActivationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
        IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
        IOptions<SiloRuntimeOptions> siloOptions = _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();

        var callInvoker = new LocalGrainCallInvoker(
            ActivationTable, typeRegistry, directory,
            _serviceProvider, siloOptions,
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance);

        CallInvoker = callInvoker;
        grainFactoryRef = new LocalGrainFactory(proxyRegistry, interfaceRegistry, callInvoker);
        Client = new LocalClusterClient(grainFactoryRef);
    }

    public IClusterClient Client { get; }
    public IGrainCallInvoker CallInvoker { get; }
    public GrainActivationTable ActivationTable { get; }
    public FakeTimeProvider Clock { get; }
    public Gate Gate { get; }
    public EntryLog EntryLog { get; }

    public async ValueTask DisposeAsync()
    {
        await ActivationTable.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }
}
