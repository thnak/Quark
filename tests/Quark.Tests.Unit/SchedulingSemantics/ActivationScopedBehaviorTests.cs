using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Part A slice (a): verifies the per-activation behavior model (<see cref="IActivationBehavior"/>).
///     A single behavior instance + a single service scope live for the whole activation — constructed
///     once, reused for every call (so instance fields persist across calls), and disposed on
///     deactivation. Reentrant + activation-scoped is rejected. Drives the real
///     <see cref="LocalGrainCallInvoker"/> dispatch path so it exercises the actual fast path, not a
///     direct mailbox post.
/// </summary>
public sealed class ActivationScopedBehaviorTests
{
    [Fact]
    public async Task ActivationBehavior_ConstructedOnce_InstanceReusedAcrossCalls()
    {
        await using var f = new Fixture();
        GrainId id = GrainId.Create(new GrainType("ActCounter"), "a");

        long r1 = await f.Invoker.InvokeAsync<IncrementInvokable, long>(id, default);
        long r2 = await f.Invoker.InvokeAsync<IncrementInvokable, long>(id, default);
        long r3 = await f.Invoker.InvokeAsync<IncrementInvokable, long>(id, default);

        // Field persisted across calls → same instance handled all three.
        Assert.Equal(1, r1);
        Assert.Equal(2, r2);
        Assert.Equal(3, r3);
        // Constructed exactly once for the activation, not once per call.
        Assert.Equal(1, f.Probe.Constructions);
    }

    [Fact]
    public async Task ActivationBehavior_Deactivation_DisposesActivationScope_AndRunsOnDeactivateOnSameInstance()
    {
        await using var f = new Fixture();
        GrainId id = GrainId.Create(new GrainType("ActCounter"), "b");

        await f.Invoker.InvokeAsync<IncrementInvokable, long>(id, default);
        await f.Invoker.InvokeAsync<IncrementInvokable, long>(id, default);

        Assert.Equal(0, f.Probe.ScopeDisposals);

        // Deactivate the activation and wait for teardown.
        Assert.True(f.ActivationTable.TryGetActivation(id, out GrainActivation? activation));
        await activation!.DisposeAsync();

        // OnDeactivateAsync saw the accumulated count (same cached instance), and the activation scope
        // (and the scoped IDisposable resolved into it) was disposed exactly once.
        Assert.Equal(2, f.Probe.DeactivateObservedCount);
        Assert.Equal(1, f.Probe.ScopeDisposals);
    }

    [Fact]
    public async Task ActivationBehavior_FreshActivation_StartsWithFreshState()
    {
        await using var f = new Fixture();
        GrainId id = GrainId.Create(new GrainType("ActCounter"), "c");

        Assert.Equal(1, await f.Invoker.InvokeAsync<IncrementInvokable, long>(id, default));

        Assert.True(f.ActivationTable.TryGetActivation(id, out GrainActivation? activation));
        await activation!.DisposeAsync();

        // A new activation of the same grain id constructs a new instance with fresh field state.
        Assert.Equal(1, await f.Invoker.InvokeAsync<IncrementInvokable, long>(id, default));
        Assert.Equal(2, f.Probe.Constructions);
    }

    [Fact]
    public async Task ReentrantActivationBehavior_Throws_NotSupported()
    {
        await using var f = new Fixture();
        GrainId id = GrainId.Create(new GrainType("ReentrantAct"), "r");

        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await f.Invoker.InvokeVoidAsync<NoopInvokable>(id, default));
    }

    // ----- hand-wired invokables -----

    private readonly struct IncrementInvokable : IGrainInvokable<long>
    {
        public uint MethodId => 0u;
        public ValueTask<long> Invoke(IGrainBehavior behavior) => new(((IActivationCounterGrain)behavior).IncrementAsync());
        public void Serialize(ref CodecWriter writer) { }
        public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
    }

    private readonly struct NoopInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 0u;
        public ValueTask Invoke(IGrainBehavior behavior) => new(((IReentrantActGrain)behavior).NoopAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    // ----- test grains / behaviors -----

    public interface IActivationCounterGrain : IGrainWithStringKey
    {
        Task<long> IncrementAsync();
    }

    public interface IReentrantActGrain : IGrainWithStringKey
    {
        Task NoopAsync();
    }

    // A per-activation behavior with mutable field state (allowed for IActivationBehavior).
    private sealed class ActivationCounterBehavior : IActivationBehavior, IActivationCounterGrain, IActivationLifecycle
    {
        private readonly Probe _probe;
        private long _count;

        public ActivationCounterBehavior(Probe probe, ScopedResource _)
        {
            _probe = probe;
            _probe.Constructions++;
        }

        public Task<long> IncrementAsync() => Task.FromResult(++_count);

        public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
        {
            _probe.DeactivateObservedCount = _count; // must see the cached instance's accumulated state
            return Task.CompletedTask;
        }
    }

    [Reentrant]
    private sealed class ReentrantActivationBehavior : IActivationBehavior, IReentrantActGrain
    {
        public Task NoopAsync() => Task.CompletedTask;
    }

    // Singleton observation sink.
    private sealed class Probe
    {
        public int Constructions;
        public int ScopeDisposals;
        public long DeactivateObservedCount;
    }

    // Scoped resource resolved into the activation scope; its disposal proves the scope was disposed.
    private sealed class ScopedResource(Probe probe) : IDisposable
    {
        public void Dispose() => probe.ScopeDisposals++;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _sp;

        public Fixture()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.Configure<SiloRuntimeOptions>(o =>
            {
                o.ClusterId = "test";
                o.ServiceId = "activation-scope";
                o.SiloName = "silo0";
            });

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
            services.AddSingleton<IUserServiceProviderRegistry, UserServiceProviderRegistry>();
            services.AddSingleton<QuarkOnlyServiceProviderHolder>();

            Probe = new Probe();
            services.AddSingleton(Probe);
            services.AddScoped<ScopedResource>();

            _sp = services.BuildServiceProvider();

            GrainTypeRegistry typeRegistry = _sp.GetRequiredService<GrainTypeRegistry>();
            typeRegistry.Register(new GrainType("ActCounter"), typeof(ActivationCounterBehavior));
            typeRegistry.Register(new GrainType("ReentrantAct"), typeof(ReentrantActivationBehavior));

            ActivationTable = _sp.GetRequiredService<GrainActivationTable>();
            Invoker = new LocalGrainCallInvoker(
                ActivationTable,
                typeRegistry,
                _sp.GetRequiredService<IGrainDirectory>(),
                _sp,
                _sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
                NullLogger<LocalGrainCallInvoker>.Instance,
                NullLogger<GrainActivation>.Instance);
        }

        public Probe Probe { get; }
        public GrainActivationTable ActivationTable { get; }
        public IGrainCallInvoker Invoker { get; }

        public async ValueTask DisposeAsync()
        {
            await ActivationTable.DisposeAsync();
            await _sp.DisposeAsync();
        }
    }
}
