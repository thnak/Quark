using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Persistence;

public sealed class InMemoryStorageTests
{
    [Fact]
    public async Task Write_And_Read_RoundTrips_DeepCopied_State()
    {
        ServiceCollection services = new();
        services.AddQuarkSerialization();
        services.AddMemoryGrainStorage();
        services.AddSingleton<IDeepCopier<CounterState>, CounterStateCopier>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IStorage<CounterState> storage = provider.GetRequiredService<IStorage<CounterState>>();

        GrainId grainId = new(new GrainType("CounterGrain"), "counter-1");
        CounterState original = new() { Value = 7 };

        await storage.WriteAsync(grainId, original);
        original.Value = 99;

        CounterState loaded = await storage.ReadAsync(grainId);

        Assert.NotSame(original, loaded);
        Assert.Equal(7, loaded.Value);
    }

    [Fact]
    public async Task Clear_Removes_Previously_Written_State()
    {
        ServiceCollection services = new();
        services.AddQuarkSerialization();
        services.AddMemoryGrainStorage();
        services.AddSingleton<IDeepCopier<CounterState>, CounterStateCopier>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IStorage<CounterState> storage = provider.GetRequiredService<IStorage<CounterState>>();

        GrainId grainId = new(new GrainType("CounterGrain"), "counter-2");
        await storage.WriteAsync(grainId, new CounterState { Value = 3 });
        await storage.ClearAsync(grainId);

        CounterState loaded = await storage.ReadAsync(grainId);
        Assert.Equal(0, loaded.Value);
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
}
