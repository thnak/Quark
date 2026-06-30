using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Testing.Harness;
using Xunit;

namespace Quark.Tests.Integration;

[Trait("category", "integration")]
public sealed class IdleTimeoutIntegrationTests
{
    // -----------------------------------------------------------------------
    // Test 1: grain deactivates after idle timeout
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grain_IsDeactivated_AfterIdleTimeout()
    {
        var counter = new DeactivationCounter();

        await using var cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.Configure<SiloRuntimeOptions>(o =>
                {
                    o.GrainCollectionAge      = TimeSpan.FromMilliseconds(100);
                    o.GrainCollectionInterval = TimeSpan.FromMilliseconds(30);
                });
                services.AddSingleton(counter);

                services.AddGrainBehavior<IIdleGrain, IdleGrainBehavior>();
                services.AddScoped<IActivationMemory<IdleGrainState>>(sp =>
                    new ActivationMemoryAccessor<IdleGrainState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<IdleGrainState>()));
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IIdleGrain, IdleGrainProxy>();
            };
        });

        IIdleGrain grain = cluster.Client.GetGrain<IIdleGrain>("idle-test");
        await grain.PingAsync();              // activate + record last-access time

        // Wait long enough for GrainCollectionAge (100ms) to expire and the
        // collector to run at least once (interval 30ms → 2-3 ticks before 200ms).
        await Task.Delay(250);

        Assert.True(counter.Count >= 1,
            $"Expected OnDeactivateAsync to be called at least once, got {counter.Count}");
    }

    // -----------------------------------------------------------------------
    // Test 2: DelayDeactivation keeps the grain alive past the idle timeout
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DelayDeactivation_PreventsIdleDeactivation()
    {
        var counter = new DeactivationCounter();

        await using var cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.Configure<SiloRuntimeOptions>(o =>
                {
                    o.GrainCollectionAge      = TimeSpan.FromMilliseconds(100);
                    o.GrainCollectionInterval = TimeSpan.FromMilliseconds(30);
                });
                services.AddSingleton(counter);

                services.AddGrainBehavior<IDelayGrain, DelayGrainBehavior>();
                services.AddScoped<IActivationMemory<DelayGrainState>>(sp =>
                    new ActivationMemoryAccessor<DelayGrainState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<DelayGrainState>()));
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IDelayGrain, DelayGrainProxy>();
            };
        });

        IDelayGrain grain = cluster.Client.GetGrain<IDelayGrain>("delay-test");
        // PingAndDelayAsync calls DelayDeactivation(30 seconds) inside the grain.
        await grain.PingAndDelayAsync();

        // Wait long enough for the idle-timeout window to expire.
        await Task.Delay(250);

        // Grain should still be alive because it called DelayDeactivation.
        Assert.Equal(0, counter.Count);
    }

    // -----------------------------------------------------------------------
    // Shared infrastructure
    // -----------------------------------------------------------------------

    /// <summary>Singleton injected into both grain types; incremented in OnDeactivateAsync.</summary>
    public sealed class DeactivationCounter
    {
        public volatile int Count;
    }

    // ----- IdleGrain: does nothing special, expects to be collected -----

    public interface IIdleGrain : IGrainWithStringKey
    {
        Task PingAsync();
    }

    private sealed class IdleGrainState { }

    private sealed class IdleGrainBehavior : IGrainBehavior, IIdleGrain, IActivationLifecycle
    {
        private readonly DeactivationCounter _counter;

        public IdleGrainBehavior(DeactivationCounter counter,
            IActivationMemory<IdleGrainState> _) // injected to satisfy DI validation
        {
            _counter = counter;
        }

        public Task PingAsync() => Task.CompletedTask;

        public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref _counter.Count);
            return Task.CompletedTask;
        }
    }

    private readonly struct IdleGrain_PingInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 0u;
        public ValueTask Invoke(IGrainBehavior behavior) => new(((IIdleGrain)behavior).PingAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    private sealed class IdleGrainProxy : IIdleGrain, IGrainProxyActivator<IdleGrainProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public IdleGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        { _grainId = grainId; _invoker = invoker; }

        public static IdleGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task PingAsync()
            => _invoker.InvokeVoidAsync(_grainId, new IdleGrain_PingInvokable()).AsTask();
    }

    // ----- DelayGrain: calls DelayDeactivation, should NOT be collected -----

    public interface IDelayGrain : IGrainWithStringKey
    {
        Task PingAndDelayAsync();
    }

    private sealed class DelayGrainState { }

    private sealed class DelayGrainBehavior : IGrainBehavior, IDelayGrain, IActivationLifecycle
    {
        private readonly DeactivationCounter _counter;
        private readonly IActivationShellAccessor _shellAccessor;

        public DelayGrainBehavior(DeactivationCounter counter, IActivationShellAccessor shellAccessor,
            IActivationMemory<DelayGrainState> _) // injected to satisfy DI validation
        {
            _counter = counter;
            _shellAccessor = shellAccessor;
        }

        public Task PingAndDelayAsync()
        {
            // Push the deactivation deadline 30 seconds into the future —
            // well beyond the 100ms GrainCollectionAge used in the test.
            _shellAccessor.Shell.DelayDeactivation(TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }

        public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref _counter.Count);
            return Task.CompletedTask;
        }
    }

    private readonly struct DelayGrain_PingInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 0u;
        public ValueTask Invoke(IGrainBehavior behavior) => new(((IDelayGrain)behavior).PingAndDelayAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    private sealed class DelayGrainProxy : IDelayGrain, IGrainProxyActivator<DelayGrainProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public DelayGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        { _grainId = grainId; _invoker = invoker; }

        public static DelayGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task PingAndDelayAsync()
            => _invoker.InvokeVoidAsync(_grainId, new DelayGrain_PingInvokable()).AsTask();
    }
}
