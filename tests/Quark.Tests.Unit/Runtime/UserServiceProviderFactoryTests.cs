using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class UserServiceProviderFactoryTests
{
    [Fact]
    public void UserServiceProviderRegistry_TryGet_ReturnsFalse_WhenNotRegistered()
    {
        var registry = new UserServiceProviderRegistry();
        Assert.False(registry.TryGet(new GrainType("Unregistered"), out _));
    }

    [Fact]
    public void UserServiceProviderRegistry_TryGet_ReturnsRegisteredProvider()
    {
        var registry = new UserServiceProviderRegistry();
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var grainType = new GrainType("Widget");

        registry.Register(grainType, provider);

        Assert.True(registry.TryGet(grainType, out IServiceProvider? found));
        Assert.Same(provider, found);
    }

    [Fact]
    public void UserServiceProviderRegistry_Register_Throws_OnNullProvider()
    {
        var registry = new UserServiceProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(new GrainType("Widget"), null!));
    }

    [Fact]
    public void QuarkOnlyServiceProviderHolder_DefaultsToNull()
    {
        Assert.Null(new QuarkOnlyServiceProviderHolder().Provider);
    }

    [Fact]
    public async Task OptedInBehavior_UserFactory_RunsOnce_ReusedAcrossCalls()
    {
        var callCount = 0;
        ServiceCollection services = CreateServices();

        services.AddGrainBehavior<ICountingGrain, CountingBehavior>(
            behaviorId: "CountingGrain",
            factory: static sp => new CountingBehavior(sp.GetRequiredService<Counter>()));
        services.AddGrainUserServiceProviderFactory<ICountingGrain, CountingBehavior>(behaviorId: "CountingGrain");
        services.AddSingleton(new UserFactoryProbe(() => callCount++));

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("CountingGrain"), "counter-1");

        int first = await invoker.InvokeAsync<IncrementInvokable, int>(grainId, new IncrementInvokable(), CancellationToken.None);
        int second = await invoker.InvokeAsync<IncrementInvokable, int>(grainId, new IncrementInvokable(), CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(1, callCount); // CreateUserServiceProvider ran exactly once, not once per call.
    }

    [Fact]
    public async Task NonOptedInBehavior_IsUnaffected()
    {
        ServiceCollection services = CreateServices();
        services.AddGrainBehavior<ICountingGrain, PlainCountingBehavior>(
            behaviorId: "PlainCountingGrain",
            factory: static sp => new PlainCountingBehavior(sp.GetRequiredService<Counter>()));

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("PlainCountingGrain"), "counter-2");

        int result = await invoker.InvokeAsync<IncrementInvokable, int>(grainId, new IncrementInvokable(), CancellationToken.None);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task OptedInBehavior_QuarkServicesResolveFromEngine_NotFromUserProvider()
    {
        ServiceCollection services = CreateServices();

        // Deliberately return a provider that ALSO has ICallContext registered — a misuse scenario.
        // The engine's real per-call ICallContext must still win (structural guarantee, not convention).
        services.AddGrainBehavior<ITenantGrain, TenantBehavior>(
            behaviorId: "TenantGrain",
            factory: static sp => new TenantBehavior(sp.GetRequiredService<ICallContext>()));
        services.AddGrainUserServiceProviderFactory<ITenantGrain, TenantBehavior>(behaviorId: "TenantGrain");

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("TenantGrain"), "tenant-xyz");

        string result = await invoker.InvokeAsync<GetGrainKeyInvokable, string>(grainId, new GetGrainKeyInvokable(), CancellationToken.None);

        Assert.Equal("tenant-xyz", result);
    }

    [Fact]
    public void CreateUserServiceProviderThrows_FailsSiloStartup_NotFirstCall()
    {
        ServiceCollection services = CreateServices();
        services.AddGrainBehavior<ICountingGrain, ThrowingFactoryBehavior>(
            behaviorId: "ThrowingFactoryGrain",
            factory: static sp => new ThrowingFactoryBehavior(sp.GetRequiredService<Counter>()));
        services.AddGrainUserServiceProviderFactory<ICountingGrain, ThrowingFactoryBehavior>(
            behaviorId: "ThrowingFactoryGrain");

        using ServiceProvider provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<GrainTypeRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration>())
        {
            reg.Apply(registry);
        }

        var userRegistry = new UserServiceProviderRegistry();
        var factoryRegistrations = provider
            .GetServices<RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration>();

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration reg in factoryRegistrations)
            {
                reg.Apply(userRegistry, provider);
            }
        });
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "user-service-provider-factory";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddSingleton<Counter>();
        return services;
    }

    private static void ApplyRegistrations(ServiceProvider provider)
    {
        var typeRegistry = provider.GetRequiredService<GrainTypeRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration>())
        {
            reg.Apply(typeRegistry);
        }

        var factoryRegistry = provider.GetRequiredService<GrainBehaviorFactoryRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration>())
        {
            reg.Apply(factoryRegistry);
        }

        var userRegistry = provider.GetRequiredService<IUserServiceProviderRegistry>();
        var factoryRegistrations = provider
            .GetServices<RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration>()
            .ToList();
        foreach (RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration reg in factoryRegistrations)
        {
            reg.Apply(userRegistry, provider);
        }

        if (factoryRegistrations.Count > 0)
        {
            var mainTypeRegistry = provider.GetRequiredService<GrainTypeRegistry>();
            var mainFactoryRegistry = provider.GetRequiredService<GrainBehaviorFactoryRegistry>();

            var quarkOnly = new ServiceCollection();
            quarkOnly.AddSingleton(mainTypeRegistry);
            quarkOnly.AddSingleton<IGrainTypeRegistry>(mainTypeRegistry);
            quarkOnly.AddSingleton(mainFactoryRegistry);
            quarkOnly.AddScoped<ActivationShellAccessor>();
            quarkOnly.AddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
            quarkOnly.AddScoped<CallContext>();
            quarkOnly.AddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
            quarkOnly.AddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
            quarkOnly.AddScoped<IBehaviorResolver, BehaviorResolver>();

            foreach (RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration marker in
                     provider.GetServices<RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration>())
            {
                marker.Apply(quarkOnly);
            }

            provider.GetRequiredService<QuarkOnlyServiceProviderHolder>().Provider = quarkOnly.BuildServiceProvider();
        }
    }

    private static LocalGrainCallInvoker CreateInvoker(ServiceProvider provider)
        => new(
            provider.GetRequiredService<GrainActivationTable>(),
            provider.GetRequiredService<IGrainTypeRegistry>(),
            provider.GetRequiredService<IGrainDirectory>(),
            provider,
            provider.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance);

    private sealed class Counter
    {
        public int Value { get; set; }
    }

    private sealed class UserFactoryProbe(Action onCreate)
    {
        public void RecordCreate() => onCreate();
    }

    private interface ICountingGrain : IGrain
    {
        Task<int> IncrementAsync();
    }

    private sealed class CountingBehavior(Counter counter) : IGrainBehavior, ICountingGrain, IGrainUserServiceProviderFactory
    {
        public Task<int> IncrementAsync()
        {
            counter.Value++;
            return Task.FromResult(counter.Value);
        }

        public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)
        {
            rootServices.GetRequiredService<UserFactoryProbe>().RecordCreate();
            return rootServices;
        }
    }

    private sealed class PlainCountingBehavior(Counter counter) : IGrainBehavior, ICountingGrain
    {
        public Task<int> IncrementAsync()
        {
            counter.Value++;
            return Task.FromResult(counter.Value);
        }
    }

    private sealed class ThrowingFactoryBehavior(Counter counter) : IGrainBehavior, ICountingGrain, IGrainUserServiceProviderFactory
    {
        public Task<int> IncrementAsync() => Task.FromResult(counter.Value);

        public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)
            => throw new InvalidOperationException("simulated startup misconfiguration");
    }

    private interface ITenantGrain : IGrain
    {
        Task<string> GetKeyAsync();
    }

    private sealed class TenantBehavior(ICallContext ctx) : IGrainBehavior, ITenantGrain, IGrainUserServiceProviderFactory
    {
        public Task<string> GetKeyAsync() => Task.FromResult(ctx.GrainId.Key);

        public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)
        {
            // Misuse: registers a decoy ICallContext into the "user" provider. The engine's real
            // per-call ICallContext must still win via CompositeServiceProvider's Quark-first ordering.
            var decoy = new ServiceCollection();
            decoy.AddSingleton<ICallContext>(new DecoyCallContext());
            return decoy.BuildServiceProvider();
        }
    }

    private sealed class DecoyCallContext : ICallContext
    {
        public GrainId GrainId => new(new GrainType("Decoy"), "decoy-key");
    }

    private readonly struct IncrementInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 1;

        public ValueTask<int> Invoke(IGrainBehavior behavior)
            => new(((ICountingGrain)behavior).IncrementAsync());

        public void Serialize(ref CodecWriter writer) { }

        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private readonly struct GetGrainKeyInvokable : IGrainInvokable<string>
    {
        public uint MethodId => 1;

        public ValueTask<string> Invoke(IGrainBehavior behavior)
            => new(((ITenantGrain)behavior).GetKeyAsync());

        public void Serialize(ref CodecWriter writer) { }

        public string DeserializeResult(ref CodecReader reader) => reader.ReadString();
    }
}
