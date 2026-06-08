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
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Integration;

public sealed class PersistenceIntegrationTests : IAsyncLifetime
{
    private PersistenceFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new PersistenceFixture();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return _fixture.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Persistent_Grain_RoundTrips_State_Across_Reactivation()
    {
        GrainId grainId = new(new GrainType("PersistentCounterGrain"), "counter-1");

        int value = await _fixture.CallInvoker.InvokeAsync<PersistentCounterBehavior_IncrementInvokable, int>(
            grainId, new PersistentCounterBehavior_IncrementInvokable());
        Assert.Equal(1, value);

        await _fixture.ActivationTable.DisposeAsync();
        _fixture.ResetActivationTable();

        int persisted = await _fixture.CallInvoker.InvokeAsync<PersistentCounterBehavior_GetValueInvokable, int>(
            grainId, new PersistentCounterBehavior_GetValueInvokable());
        Assert.Equal(1, persisted);
    }

    private sealed class PersistenceFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public PersistenceFixture()
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

            // Behavior registration
            services.AddGrainBehavior<IPersistentCounterGrain, PersistentCounterBehavior>();

            // Persistent activation memory
            services.AddScoped<IPersistentActivationMemory<CounterState>>(sp =>
                new PersistentActivationMemoryAccessor<CounterState>(
                    sp.GetRequiredService<IActivationShellAccessor>()
                      .Shell.GetOrCreateHolder<CounterState>(),
                    sp.GetRequiredService<IStorage<CounterState>>(),
                    sp.GetRequiredService<ICallContext>(),
                    StorageOptions.DefaultStateName));

            _serviceProvider = services.BuildServiceProvider();
            ResetActivationTable();
        }

        public GrainActivationTable ActivationTable { get; private set; } = null!;
        public LocalGrainCallInvoker CallInvoker { get; private set; } = null!;

        public async ValueTask DisposeAsync()
        {
            await ActivationTable.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }

        public void ResetActivationTable()
        {
            ActivationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
            CallInvoker = _serviceProvider.GetRequiredService<LocalGrainCallInvoker>();
        }
    }

    private interface IPersistentCounterGrain : IGrainWithStringKey
    {
        Task<int> IncrementAsync();
        Task<int> GetValueAsync();
    }

    private sealed class PersistentCounterBehavior : IGrainBehavior, IPersistentCounterGrain, IActivationLifecycle
    {
        private readonly IPersistentActivationMemory<CounterState> _memory;

        public PersistentCounterBehavior(IPersistentActivationMemory<CounterState> memory)
        {
            _memory = memory;
        }

        public Task OnActivateAsync(CancellationToken ct) => _memory.LoadAsync(ct);

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

        public async Task<int> IncrementAsync()
        {
            _memory.Value.Value++;
            await _memory.SaveAsync();
            return _memory.Value.Value;
        }

        public Task<int> GetValueAsync()
        {
            return Task.FromResult(_memory.Value.Value);
        }
    }

    private sealed class CounterState
    {
        public int Value { get; set; }
    }

    private sealed class CounterStateCopier : IDeepCopier<CounterState>
    {
        public CounterState DeepCopy(CounterState original, CopyContext context)
        {
            return new CounterState { Value = original.Value };
        }
    }

    private readonly struct PersistentCounterBehavior_IncrementInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 0u;
        public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((IPersistentCounterGrain)behavior).IncrementAsync());
        public void Serialize(ref CodecWriter writer) { }
        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private readonly struct PersistentCounterBehavior_GetValueInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 1u;
        public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((IPersistentCounterGrain)behavior).GetValueAsync());
        public void Serialize(ref CodecWriter writer) { }
        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private sealed class NullGrainFactory : IGrainFactory
    {
        public TGI GetGrain<TGI>(string key) where TGI : IGrainWithStringKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(long key) where TGI : IGrainWithIntegerKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(Guid key) where TGI : IGrainWithGuidKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(long key, string? ext) where TGI : IGrainWithIntegerCompoundKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(Guid key, string? ext) where TGI : IGrainWithGuidCompoundKey
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, string key)
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, Guid key)
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, long key)
        {
            throw new NotImplementedException();
        }
    }
}
