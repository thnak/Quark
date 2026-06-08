using Quark.Persistence.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Persistence;

public sealed class ManagedActivationMemoryTests
{
    [Fact]
    public async Task GetAsync_Calls_Factory_On_First_Access()
    {
        int callCount = 0;
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => { callCount++; return Task.FromResult(new MyResource()); });

        await holder.GetAsync();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetAsync_Returns_Cached_Value_On_Subsequent_Calls()
    {
        var resource = new MyResource();
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => Task.FromResult(resource));

        MyResource r1 = await holder.GetAsync();
        MyResource r2 = await holder.GetAsync();
        MyResource r3 = await holder.GetAsync();

        Assert.Same(resource, r1);
        Assert.Same(resource, r2);
        Assert.Same(resource, r3);
    }

    [Fact]
    public async Task GetAsync_Invokes_Factory_Exactly_Once()
    {
        int callCount = 0;
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => { callCount++; return Task.FromResult(new MyResource()); });

        await holder.GetAsync();
        await holder.GetAsync();
        await holder.GetAsync();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetAsync_Throws_If_Init_Not_Configured()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => holder.GetAsync().AsTask());
    }

    [Fact]
    public void IsInitialized_Returns_False_Before_GetAsync()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => Task.FromResult(new MyResource()));

        Assert.False(holder.IsInitialized);
    }

    [Fact]
    public async Task IsInitialized_Returns_True_After_GetAsync()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => Task.FromResult(new MyResource()));

        await holder.GetAsync();

        Assert.True(holder.IsInitialized);
    }

    [Fact]
    public async Task DisposeAsync_Calls_Cleanup_When_Initialized()
    {
        bool cleanupCalled = false;
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => Task.FromResult(new MyResource()))
              .Destroy(_ => { cleanupCalled = true; return Task.CompletedTask; });

        await holder.GetAsync();
        await holder.DisposeAsync();

        Assert.True(cleanupCalled);
    }

    [Fact]
    public async Task DisposeAsync_Passes_Value_To_Cleanup()
    {
        var resource = new MyResource();
        MyResource? received = null;
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => Task.FromResult(resource))
              .Destroy(r => { received = r; return Task.CompletedTask; });

        await holder.GetAsync();
        await holder.DisposeAsync();

        Assert.Same(resource, received);
    }

    [Fact]
    public async Task DisposeAsync_Does_Not_Call_Cleanup_If_Not_Initialized()
    {
        bool cleanupCalled = false;
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => Task.FromResult(new MyResource()))
              .Destroy(_ => { cleanupCalled = true; return Task.CompletedTask; });

        await holder.DisposeAsync();

        Assert.False(cleanupCalled);
    }

    [Fact]
    public async Task DisposeAsync_Skips_Cleanup_When_Destroy_Not_Configured()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => Task.FromResult(new MyResource()));

        await holder.GetAsync();
        // Should not throw even without Destroy configured.
        await holder.DisposeAsync();
    }

    [Fact]
    public void Init_Returns_Same_Interface_For_Fluent_Chaining()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        var returned = holder.Init(() => Task.FromResult(new MyResource()));

        Assert.Same(holder, returned);
    }

    [Fact]
    public void Destroy_Returns_Same_Interface_For_Fluent_Chaining()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        var returned = holder.Destroy(_ => Task.CompletedTask);

        Assert.Same(holder, returned);
    }

    private sealed class MyResource { }
}
