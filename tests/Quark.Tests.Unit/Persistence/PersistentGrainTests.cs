using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Persistence;

public sealed class PersistentGrainTests
{
    [Fact]
    public async Task PersistentBehavior_Loads_Previously_Written_State_On_Activate()
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddMemoryGrainStorage();
        services.AddSingleton<IDeepCopier<PersistentCounterState>, PersistentCounterStateCopier>();

        using ServiceProvider provider = services.BuildServiceProvider();
        GrainId id = new(new GrainType("PersistentCounterGrain"), "1");
        var storage = provider.GetRequiredService<IStorage<PersistentCounterState>>();

        // First activation: write state
        var ctx1 = new FixedCallContext(id);
        var holder1 = new StateHolder<PersistentCounterState>();
        var mem1 = new PersistentActivationMemoryAccessor<PersistentCounterState>(
            holder1, storage, ctx1, StorageOptions.DefaultStateName);
        var behavior1 = new PersistentCounterBehavior(mem1);
        await behavior1.OnActivateAsync(CancellationToken.None);
        await behavior1.SetValueAsync(12);

        // Second activation: reload state
        var ctx2 = new FixedCallContext(id);
        var holder2 = new StateHolder<PersistentCounterState>();
        var mem2 = new PersistentActivationMemoryAccessor<PersistentCounterState>(
            holder2, storage, ctx2, StorageOptions.DefaultStateName);
        var behavior2 = new PersistentCounterBehavior(mem2);
        await behavior2.OnActivateAsync(CancellationToken.None);

        Assert.Equal(12, behavior2.CurrentValue);
    }

    private sealed class PersistentCounterState
    {
        public int Value { get; set; }
    }

    private sealed class PersistentCounterBehavior : IGrainBehavior, IActivationLifecycle
    {
        private readonly IPersistentActivationMemory<PersistentCounterState> _memory;

        public PersistentCounterBehavior(IPersistentActivationMemory<PersistentCounterState> memory)
            => _memory = memory;

        public int CurrentValue => _memory.Value.Value;

        public Task OnActivateAsync(CancellationToken ct) => _memory.LoadAsync(ct);

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

        public async Task SetValueAsync(int value)
        {
            _memory.Value.Value = value;
            await _memory.SaveAsync();
        }
    }

    private sealed class PersistentCounterStateCopier : IDeepCopier<PersistentCounterState>
    {
        public PersistentCounterState DeepCopy(PersistentCounterState original, CopyContext context)
            => new() { Value = original.Value };
    }

    private sealed class FixedCallContext(GrainId grainId) : ICallContext
    {
        public GrainId GrainId => grainId;
    }
}
