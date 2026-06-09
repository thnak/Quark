using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Integration;

// ---------------------------------------------------------------------------
// Resource, grain interface, behavior
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Invokables (normally generated)
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Proxy (normally generated)
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Fixture
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class ManagedMemoryGrainTests : IAsyncLifetime
{
    private ManagedMemoryFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new ManagedMemoryFixture();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _fixture.DisposeAsync().AsTask();

    private IManagedBufferGrain GetGrain(string key)
        => _fixture.Client.GetGrain<IManagedBufferGrain>(key);

    [Fact]
    public async Task Factory_Called_On_First_Access()
    {
        IManagedBufferGrain grain = GetGrain("init-count");
        long count = await grain.GetInitCountAsync();
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task Factory_Called_Exactly_Once_Per_Activation()
    {
        IManagedBufferGrain grain = GetGrain("factory-once");
        await grain.GetInitCountAsync();
        await grain.GetInitCountAsync();
        long count = await grain.GetInitCountAsync();
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task Resource_Persists_Across_Calls()
    {
        IManagedBufferGrain grain = GetGrain("persist-data");
        await grain.SetDataAsync("hello");
        string data = await grain.GetDataAsync();
        Assert.Equal("hello", data);
    }

    [Fact]
    public async Task Deactivation_Invokes_Destroy_Callback()
    {
        IManagedBufferGrain grain = GetGrain("destroy-test");
        await grain.GetDataAsync(); // trigger init

        var grainId = new GrainId(new GrainType("ManagedBufferGrain"), "destroy-test");
        _fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation);
        ManagedActivationMemoryHolder<ManagedBuffer> holder = activation!.GetOrCreateManagedHolder<ManagedBuffer>();

        await grain.SelfDestructAsync();
        await Task.Delay(200);

        // The destroy callback increments DestroyCount on the buffer.
        ManagedBuffer buf = await holder.GetAsync();
        Assert.Equal(1, buf.DestroyCount);
    }

    [Fact]
    public async Task Deactivation_Does_Not_Throw_When_Resource_Never_Accessed()
    {
        IManagedBufferGrain grain = GetGrain("no-access-test");
        // Force activation by making a grain call that doesn't touch managed memory directly.
        // SelfDestruct triggers deactivation; the holder was never initialized.
        await grain.SelfDestructAsync();
        await Task.Delay(200);
        // No exception expected — idle holders skip cleanup.
    }
}
