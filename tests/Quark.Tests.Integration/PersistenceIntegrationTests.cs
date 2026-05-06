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

public sealed class PersistenceIntegrationTests : IAsyncLifetime
{
    private PersistenceFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new PersistenceFixture();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _fixture.DisposeAsync().AsTask();

    [Fact]
    public async Task Persistent_Grain_RoundTrips_State_Across_Reactivation()
    {
        GrainId grainId = new(new GrainType("PersistentCounterGrain"), "counter-1");

        int value = await _fixture.CallInvoker.InvokeAsync<int>(grainId, PersistentCounterGrainMethodInvoker.IncrementMethodId);
        Assert.Equal(1, value);

        await _fixture.ActivationTable.DisposeAsync();
        _fixture.ResetActivationTable();

        int persisted = await _fixture.CallInvoker.InvokeAsync<int>(grainId, PersistentCounterGrainMethodInvoker.GetValueMethodId);
        Assert.Equal(1, persisted);
    }

    private sealed class PersistenceFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        public GrainActivationTable ActivationTable { get; private set; } = null!;
        public LocalGrainCallInvoker CallInvoker { get; private set; } = null!;

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
            services.AddTransient<PersistentCounterGrain>();
            services.AddSingleton<PersistentCounterGrainMethodInvoker>();

            _serviceProvider = services.BuildServiceProvider();
            ResetActivationTable();
        }

        public void ResetActivationTable()
        {
            GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
            typeRegistry.Register(new GrainType("PersistentCounterGrain"), typeof(PersistentCounterGrain));

            GrainMethodInvokerRegistry invokerRegistry = _serviceProvider.GetRequiredService<GrainMethodInvokerRegistry>();
            invokerRegistry.Register(typeof(PersistentCounterGrain), _serviceProvider.GetRequiredService<PersistentCounterGrainMethodInvoker>());

            ActivationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
            CallInvoker = new LocalGrainCallInvoker(
                ActivationTable,
                _serviceProvider.GetRequiredService<IGrainActivator>(),
                typeRegistry,
                _serviceProvider.GetRequiredService<IGrainDirectory>(),
                _serviceProvider.GetRequiredService<IGrainMethodInvokerRegistry>(),
                _serviceProvider.GetRequiredService<IGrainFactory>(),
                _serviceProvider,
                _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
                NullLogger<LocalGrainCallInvoker>.Instance,
                NullLogger<GrainActivation>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            await ActivationTable.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }
    }

    private sealed class PersistentCounterGrain : Grain<CounterState>
    {
        public async Task<int> IncrementAsync()
        {
            State.Value++;
            await WriteStateAsync();
            return State.Value;
        }

        public Task<int> GetValueAsync() => Task.FromResult(State.Value);
    }

    private sealed class CounterState
    {
        public int Value { get; set; }
    }

    private sealed class CounterStateCopier : IDeepCopier<CounterState>
    {
        public CounterState DeepCopy(CounterState original, CopyContext context) => new() { Value = original.Value };
    }

    private sealed class PersistentCounterGrainMethodInvoker : IGrainMethodInvoker
    {
        public const uint IncrementMethodId = 0;
        public const uint GetValueMethodId = 1;

        public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
        {
            PersistentCounterGrain typed = (PersistentCounterGrain)grain;
            return methodId switch
            {
                IncrementMethodId => await typed.IncrementAsync(),
                GetValueMethodId => await typed.GetValueAsync(),
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
