using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Xunit;

namespace Quark.Tests.Integration;

/// <summary>
///     Verifies F-04: <c>IPersistentState&lt;T&gt;</c> injection via <c>[PersistentState]</c>,
///     multiple independent named slots, and explicit read/write lifecycle.
/// </summary>
public sealed class PersistentStateInjectionTests : IAsyncLifetime
{
    private PersistentStateFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new PersistentStateFixture();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _fixture.DisposeAsync().AsTask();

    [Fact]
    public async Task Two_PersistentState_Slots_Are_Independent()
    {
        var grainId = new GrainId(new GrainType("TwoSlotGrain"), "slot-test");

        // Increment counter A twice
        await _fixture.Invoker.InvokeAsync(grainId, TwoSlotGrainMethodInvoker.IncrementAMethodId);
        await _fixture.Invoker.InvokeAsync(grainId, TwoSlotGrainMethodInvoker.IncrementAMethodId);

        // Increment counter B once
        await _fixture.Invoker.InvokeAsync(grainId, TwoSlotGrainMethodInvoker.IncrementBMethodId);

        int a = await _fixture.Invoker.InvokeAsync<int>(grainId, TwoSlotGrainMethodInvoker.GetAMethodId);
        int b = await _fixture.Invoker.InvokeAsync<int>(grainId, TwoSlotGrainMethodInvoker.GetBMethodId);

        Assert.Equal(2, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public async Task PersistentState_Survives_Reactivation()
    {
        var grainId = new GrainId(new GrainType("TwoSlotGrain"), "persist-test");

        await _fixture.Invoker.InvokeAsync(grainId, TwoSlotGrainMethodInvoker.IncrementAMethodId);

        // Simulate deactivation + reactivation
        await _fixture.ActivationTable.DisposeAsync();
        _fixture.ResetActivationTable();

        // On new activation the grain reads state explicitly in OnActivateAsync
        int a = await _fixture.Invoker.InvokeAsync<int>(grainId, TwoSlotGrainMethodInvoker.GetAMethodId);
        Assert.Equal(1, a);
    }

    // -----------------------------------------------------------------------
    // Fixture
    // -----------------------------------------------------------------------

    private sealed class PersistentStateFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public PersistentStateFixture()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddQuarkSerialization();
            services.AddMemoryGrainStorage();
            services.AddSingleton<IDeepCopier<CounterState>, CounterStateCopier>();
            services.Configure<SiloRuntimeOptions>(o =>
            {
                o.ClusterId = "test";
                o.ServiceId = "integration";
                o.SiloName = "silo0";
            });
            services.AddSingleton<IGrainFactory, NullGrainFactory>();
            services.AddQuarkRuntime();
            services.AddSingleton<IGrainActivatorFactory>(new TwoSlotGrainActivatorFactory());
            services.AddSingleton<TwoSlotGrainMethodInvoker>();

            _serviceProvider = services.BuildServiceProvider();
            ResetActivationTable();
        }

        public GrainActivationTable ActivationTable { get; private set; } = null!;
        public LocalGrainCallInvoker Invoker { get; private set; } = null!;

        public async ValueTask DisposeAsync()
        {
            await ActivationTable.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }

        public void ResetActivationTable()
        {
            GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
            typeRegistry.Register(new GrainType("TwoSlotGrain"), typeof(TwoSlotGrain));

            GrainMethodInvokerRegistry invokerRegistry =
                _serviceProvider.GetRequiredService<GrainMethodInvokerRegistry>();
            invokerRegistry.Register(typeof(TwoSlotGrain),
                _serviceProvider.GetRequiredService<TwoSlotGrainMethodInvoker>());

            ActivationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
            Invoker = new LocalGrainCallInvoker(
                ActivationTable,
                _serviceProvider.GetRequiredService<IGrainActivator>(),
                typeRegistry,
                _serviceProvider.GetRequiredService<IGrainDirectory>(),
                _serviceProvider.GetRequiredService<IGrainMethodInvokerRegistry>(),
                _serviceProvider,
                _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
                NullLogger<LocalGrainCallInvoker>.Instance,
                NullLogger<GrainActivation>.Instance);
        }
    }

    // -----------------------------------------------------------------------
    // Grain with two [PersistentState] slots
    // -----------------------------------------------------------------------

    private sealed class TwoSlotGrain : Grain
    {
        private readonly IPersistentState<CounterState> _counterA;
        private readonly IPersistentState<CounterState> _counterB;

        public TwoSlotGrain(
            IPersistentState<CounterState> counterA,
            IPersistentState<CounterState> counterB)
        {
            _counterA = counterA;
            _counterB = counterB;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            await _counterA.ReadStateAsync(cancellationToken);
            await _counterB.ReadStateAsync(cancellationToken);
        }

        public async Task IncrementAAsync()
        {
            _counterA.State.Value++;
            await _counterA.WriteStateAsync();
        }

        public async Task IncrementBAsync()
        {
            _counterB.State.Value++;
            await _counterB.WriteStateAsync();
        }

        public Task<int> GetAAsync() => Task.FromResult(_counterA.State.Value);
        public Task<int> GetBAsync() => Task.FromResult(_counterB.State.Value);
    }

    private sealed class CounterState
    {
        public int Value { get; set; }
    }

    private sealed class CounterStateCopier : IDeepCopier<CounterState>
    {
        public CounterState DeepCopy(CounterState original, CopyContext context)
            => new() { Value = original.Value };
    }

    // Hand-written activator factory that mirrors what the code generator will emit
    private sealed class TwoSlotGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(TwoSlotGrain);

        public Grain Create(GrainId grainId, IServiceProvider services)
        {
            IGrainStorage storage = Microsoft.Extensions.DependencyInjection
                .ServiceProviderServiceExtensions.GetRequiredService<IGrainStorage>(services);
            return new TwoSlotGrain(
                new PersistentState<CounterState>(grainId, "counterA", storage),
                new PersistentState<CounterState>(grainId, "counterB", storage));
        }
    }

    private sealed class TwoSlotGrainMethodInvoker : IGrainMethodInvoker
    {
        public const uint IncrementAMethodId = 0;
        public const uint IncrementBMethodId = 1;
        public const uint GetAMethodId = 2;
        public const uint GetBMethodId = 3;

        public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
        {
            var typed = (TwoSlotGrain)grain;
            return methodId switch
            {
                IncrementAMethodId => await typed.IncrementAAsync().ContinueWith(_ => (object?)null),
                IncrementBMethodId => await typed.IncrementBAsync().ContinueWith(_ => (object?)null),
                GetAMethodId => await typed.GetAAsync(),
                GetBMethodId => await typed.GetBAsync(),
                _ => throw new NotSupportedException($"Unknown method id {methodId}")
            };
        }
    }

    private sealed class NullGrainFactory : IGrainFactory
    {
        public TGI GetGrain<TGI>(string key) where TGI : IGrainWithStringKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(long key) where TGI : IGrainWithIntegerKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(Guid key) where TGI : IGrainWithGuidKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(long key, string? ext) where TGI : IGrainWithIntegerCompoundKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(Guid key, string? ext) where TGI : IGrainWithGuidCompoundKey => throw new NotImplementedException();
        public IGrain GetGrain(Type t, string key) => throw new NotImplementedException();
        public IGrain GetGrain(Type t, Guid key) => throw new NotImplementedException();
        public IGrain GetGrain(Type t, long key) => throw new NotImplementedException();
    }
}
