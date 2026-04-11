using Quark.Core.Abstractions;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Quark.Tests.Unit.Persistence;

public sealed class PersistentGrainTests
{
    [Fact]
    public async Task GrainOfT_Loads_Previously_Written_State_On_Activate()
    {
        ServiceCollection services = new();
        services.AddQuarkSerialization();
        services.AddMemoryGrainStorage();
        services.AddSingleton<IDeepCopier<CounterState>, CounterStateCopier>();

        using ServiceProvider provider = services.BuildServiceProvider();
        GrainId id = new(new GrainType("PersistentCounterGrain"), "1");

        var grain1 = new PersistentCounterGrain();
        var ctx1 = new GrainContext(id, new NullGrainFactory(), provider);
        await ctx1.ActivateAsync(grain1);
        await grain1.SetValueAsync(12);
        await ctx1.DeactivateAsync(grain1, DeactivationReason.ApplicationRequested);

        var grain2 = new PersistentCounterGrain();
        var ctx2 = new GrainContext(id, new NullGrainFactory(), provider);
        await ctx2.ActivateAsync(grain2);

        Assert.Equal(12, grain2.CurrentValue);
    }

    private sealed class PersistentCounterGrain : Grain<CounterState>
    {
        public int CurrentValue => State.Value;

        public async Task SetValueAsync(int value)
        {
            State.Value = value;
            await WriteStateAsync();
        }
    }

    private sealed class CounterState
    {
        public int Value { get; set; }
    }

    private sealed class CounterStateCopier : IDeepCopier<CounterState>
    {
        public CounterState DeepCopy(CounterState original, CopyContext context) =>
            new() { Value = original.Value };
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
