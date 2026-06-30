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

public sealed class GrainScopeInitializerTests
{
    [Fact]
    public async Task InitializerRunsAfterCallContextBindAndBeforeBehaviorConstruction()
    {
        var events = new List<string>();
        var services = CreateServices(events);

        services.AddGrainBehavior<IInitializableGrain, InitializableBehavior>();
        services.AddGrainScopeInitializer<IInitializableGrain, InitializableBehavior>((ctx, scopedProvider, ct) =>
        {
            events.Add($"init:{ctx.GrainId.Key}");
            scopedProvider.GetRequiredService<ScopedTenant>().Value = ctx.GrainId.Key;
            return ValueTask.CompletedTask;
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("InitializableGrain"), "tenant-a/order-1");

        string result = await invoker.InvokeAsync<GetTenantInvokable, string>(
            grainId,
            new GetTenantInvokable(),
            CancellationToken.None);

        Assert.Equal("tenant-a/order-1", result);
        Assert.Equal(
            [
                "init:tenant-a/order-1",
                "ctor:tenant-a/order-1",
                "activate:tenant-a/order-1",
                "init:tenant-a/order-1",
                "ctor:tenant-a/order-1",
                "call:tenant-a/order-1",
            ],
            events);
    }

    [Fact]
    public async Task InitializerRunsBeforeOnDeactivateAsync()
    {
        var events = new List<string>();
        var services = CreateServices(events);

        services.AddGrainBehavior<IInitializableGrain, InitializableBehavior>();
        services.AddGrainScopeInitializer<IInitializableGrain, InitializableBehavior>((ctx, scopedProvider, ct) =>
        {
            events.Add($"init:{ctx.GrainId.Key}");
            scopedProvider.GetRequiredService<ScopedTenant>().Value = ctx.GrainId.Key;
            return ValueTask.CompletedTask;
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("InitializableGrain"), "tenant-b/order-2");

        await invoker.InvokeAsync<GetTenantInvokable, string>(grainId, new GetTenantInvokable(), CancellationToken.None);
        Assert.True(provider.GetRequiredService<GrainActivationTable>().TryGetActivation(grainId, out GrainActivation? activation));

        events.Clear();
        activation!.Deactivate(DeactivationReason.ShuttingDown);
        await WaitForAsync(() => activation.ActivationStatus == GrainActivationStatus.Inactive);

        Assert.Equal(
            [
                "init:tenant-b/order-2",
                "ctor:tenant-b/order-2",
                "deactivate:tenant-b/order-2",
            ],
            events);
    }

    [Fact]
    public async Task ThrowingInitializerFaultsActivationAndRemovesActivationFromTable()
    {
        var services = CreateServices([]);

        services.AddGrainBehavior<IInitializableGrain, InitializableBehavior>();
        services.AddGrainScopeInitializer<IInitializableGrain, InitializableBehavior>((ctx, scopedProvider, ct) =>
            throw new InvalidOperationException("tenant unavailable"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("InitializableGrain"), "tenant-missing/order-3");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync<GetTenantInvokable, string>(grainId, new GetTenantInvokable(), CancellationToken.None));

        Assert.Equal("tenant unavailable", ex.Message);
        Assert.False(provider.GetRequiredService<GrainActivationTable>().TryGetActivation(grainId, out _));
    }

    private static ServiceCollection CreateServices(List<string> events)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "scope-initializer";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddScoped<ScopedTenant>();
        services.AddSingleton(events);

        return services;
    }

    private static void ApplyRegistrations(ServiceProvider provider)
    {
        var typeRegistry = provider.GetRequiredService<GrainTypeRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration registration
                 in provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration>())
        {
            registration.Apply(typeRegistry);
        }

        var initializerRegistry = provider.GetRequiredService<IGrainScopeInitializerRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainScopeInitializerRegistration registration
                 in provider.GetServices<RuntimeServiceCollectionExtensions.IGrainScopeInitializerRegistration>())
        {
            registration.Apply(initializerRegistry);
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

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    private sealed class ScopedTenant
    {
        public string? Value { get; set; }
    }

    private interface IInitializableGrain : IGrain
    {
        Task<string> GetTenantAsync();
    }

    private sealed class InitializableBehavior : IGrainBehavior, IInitializableGrain, IActivationLifecycle
    {
        private readonly List<string> _events;
        private readonly ScopedTenant _tenant;

        public InitializableBehavior(ICallContext ctx, ScopedTenant tenant, List<string> events)
        {
            _tenant = tenant;
            _events = events;
            _events.Add($"ctor:{ctx.GrainId.Key}");
        }

        public Task<string> GetTenantAsync()
        {
            _events.Add($"call:{_tenant.Value}");
            return Task.FromResult(_tenant.Value ?? "");
        }

        public Task OnActivateAsync(CancellationToken ct)
        {
            _events.Add($"activate:{_tenant.Value}");
            return Task.CompletedTask;
        }

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
        {
            _events.Add($"deactivate:{_tenant.Value}");
            return Task.CompletedTask;
        }
    }

    private readonly struct GetTenantInvokable : IGrainInvokable<string>
    {
        public uint MethodId => 1;

        public ValueTask<string> Invoke(IGrainBehavior behavior)
            => new(((IInitializableGrain)behavior).GetTenantAsync());

        public void Serialize(ref CodecWriter writer) { }

        public string DeserializeResult(ref CodecReader reader) => reader.ReadString();
    }
}
