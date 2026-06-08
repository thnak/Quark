using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Integration;

/// <summary>
///     Verifies F-04: <c>IPersistentActivationMemory&lt;T&gt;</c> injection via named slots,
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
        await _fixture.Invoker.InvokeVoidAsync(grainId, new TwoSlotBehavior_IncrementAInvokable());
        await _fixture.Invoker.InvokeVoidAsync(grainId, new TwoSlotBehavior_IncrementAInvokable());

        // Increment counter B once
        await _fixture.Invoker.InvokeVoidAsync(grainId, new TwoSlotBehavior_IncrementBInvokable());

        int a = await _fixture.Invoker.InvokeAsync<TwoSlotBehavior_GetAInvokable, int>(grainId, new TwoSlotBehavior_GetAInvokable());
        int b = await _fixture.Invoker.InvokeAsync<TwoSlotBehavior_GetBInvokable, int>(grainId, new TwoSlotBehavior_GetBInvokable());

        Assert.Equal(2, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public async Task PersistentState_Survives_Reactivation()
    {
        var grainId = new GrainId(new GrainType("TwoSlotGrain"), "persist-test");

        await _fixture.Invoker.InvokeVoidAsync(grainId, new TwoSlotBehavior_IncrementAInvokable());

        // Simulate deactivation + reactivation
        await _fixture.ActivationTable.DisposeAsync();
        _fixture.ResetActivationTable();

        // On new activation the grain reads state in OnActivateAsync
        int a = await _fixture.Invoker.InvokeAsync<TwoSlotBehavior_GetAInvokable, int>(grainId, new TwoSlotBehavior_GetAInvokable());
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
            services.AddSingleton<IDeepCopier<CounterStateA>, CounterStateACopier>();
            services.AddSingleton<IDeepCopier<CounterStateB>, CounterStateBCopier>();
            services.Configure<SiloRuntimeOptions>(o =>
            {
                o.ClusterId = "test";
                o.ServiceId = "integration";
                o.SiloName = "silo0";
            });
            services.AddSingleton<IGrainFactory, NullGrainFactory>();
            services.AddQuarkRuntime();

            // Behavior registration
            services.AddGrainBehavior<ITwoSlotGrain, TwoSlotBehavior>();

            // Two independent persistent memory slots via distinct state types
            services.AddScoped<IPersistentActivationMemory<CounterStateA>>(sp =>
                new PersistentActivationMemoryAccessor<CounterStateA>(
                    sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<CounterStateA>(),
                    sp.GetRequiredService<IStorage<CounterStateA>>(),
                    sp.GetRequiredService<ICallContext>(),
                    "counterA"));

            services.AddScoped<IPersistentActivationMemory<CounterStateB>>(sp =>
                new PersistentActivationMemoryAccessor<CounterStateB>(
                    sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<CounterStateB>(),
                    sp.GetRequiredService<IStorage<CounterStateB>>(),
                    sp.GetRequiredService<ICallContext>(),
                    "counterB"));

            _serviceProvider = services.BuildServiceProvider();

            var typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
            typeRegistry.Register(new GrainType("TwoSlotGrain"), typeof(TwoSlotBehavior));

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
            ActivationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
            Invoker = _serviceProvider.GetRequiredService<LocalGrainCallInvoker>();
        }
    }

    // -----------------------------------------------------------------------
    // State types — distinct types give separate storage slots and holders
    // -----------------------------------------------------------------------

    private sealed class CounterStateA
    {
        public int Value { get; set; }
    }

    private sealed class CounterStateB
    {
        public int Value { get; set; }
    }

    private sealed class CounterStateACopier : IDeepCopier<CounterStateA>
    {
        public CounterStateA DeepCopy(CounterStateA original, CopyContext context)
            => new() { Value = original.Value };
    }

    private sealed class CounterStateBCopier : IDeepCopier<CounterStateB>
    {
        public CounterStateB DeepCopy(CounterStateB original, CopyContext context)
            => new() { Value = original.Value };
    }

    // -----------------------------------------------------------------------
    // Grain interface + behavior with two named persistent slots
    // -----------------------------------------------------------------------

    private interface ITwoSlotGrain : IGrainWithStringKey
    {
        Task IncrementAAsync();
        Task IncrementBAsync();
        Task<int> GetAAsync();
        Task<int> GetBAsync();
    }

    private sealed class TwoSlotBehavior : IGrainBehavior, ITwoSlotGrain, IActivationLifecycle
    {
        private readonly IPersistentActivationMemory<CounterStateA> _counterA;
        private readonly IPersistentActivationMemory<CounterStateB> _counterB;

        public TwoSlotBehavior(
            IPersistentActivationMemory<CounterStateA> counterA,
            IPersistentActivationMemory<CounterStateB> counterB)
        {
            _counterA = counterA;
            _counterB = counterB;
        }

        public async Task OnActivateAsync(CancellationToken ct)
        {
            await _counterA.LoadAsync(ct);
            await _counterB.LoadAsync(ct);
        }

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

        public async Task IncrementAAsync()
        {
            _counterA.Value.Value++;
            await _counterA.SaveAsync();
        }

        public async Task IncrementBAsync()
        {
            _counterB.Value.Value++;
            await _counterB.SaveAsync();
        }

        public Task<int> GetAAsync() => Task.FromResult(_counterA.Value.Value);
        public Task<int> GetBAsync() => Task.FromResult(_counterB.Value.Value);
    }

    private readonly struct TwoSlotBehavior_IncrementAInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 0u;
        public ValueTask Invoke(IGrainBehavior behavior) => new(((ITwoSlotGrain)behavior).IncrementAAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    private readonly struct TwoSlotBehavior_IncrementBInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 1u;
        public ValueTask Invoke(IGrainBehavior behavior) => new(((ITwoSlotGrain)behavior).IncrementBAsync());
        public void Serialize(ref CodecWriter writer) { }
    }

    private readonly struct TwoSlotBehavior_GetAInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 2u;
        public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((ITwoSlotGrain)behavior).GetAAsync());
        public void Serialize(ref CodecWriter writer) { }
        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private readonly struct TwoSlotBehavior_GetBInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 3u;
        public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((ITwoSlotGrain)behavior).GetBAsync());
        public void Serialize(ref CodecWriter writer) { }
        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
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
