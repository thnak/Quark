using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Hosting;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Persistence;

public sealed class ManagedActivationMemoryTests
{
    [Fact]
    public async Task GetAsync_Calls_Factory_On_First_Access()
    {
        int callCount = 0;
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => { callCount++; return ValueTask.FromResult(new MyResource()); });

        await holder.GetAsync();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetAsync_Returns_Cached_Value_On_Subsequent_Calls()
    {
        var resource = new MyResource();
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => ValueTask.FromResult(resource));

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
        holder.Init(() => { callCount++; return ValueTask.FromResult(new MyResource()); });

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
        holder.Init(() => ValueTask.FromResult(new MyResource()));

        Assert.False(holder.IsInitialized);
    }

    [Fact]
    public async Task IsInitialized_Returns_True_After_GetAsync()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => ValueTask.FromResult(new MyResource()));

        await holder.GetAsync();

        Assert.True(holder.IsInitialized);
    }

    [Fact]
    public async Task DisposeAsync_Calls_Cleanup_When_Initialized()
    {
        bool cleanupCalled = false;
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => ValueTask.FromResult(new MyResource()))
              .Destroy(_ => { cleanupCalled = true; return ValueTask.CompletedTask; });

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
        holder.Init(() => ValueTask.FromResult(resource))
              .Destroy(r => { received = r; return ValueTask.CompletedTask; });

        await holder.GetAsync();
        await holder.DisposeAsync();

        Assert.Same(resource, received);
    }

    [Fact]
    public async Task DisposeAsync_Does_Not_Call_Cleanup_If_Not_Initialized()
    {
        bool cleanupCalled = false;
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => ValueTask.FromResult(new MyResource()))
              .Destroy(_ => { cleanupCalled = true; return ValueTask.CompletedTask; });

        await holder.DisposeAsync();

        Assert.False(cleanupCalled);
    }

    [Fact]
    public async Task DisposeAsync_Skips_Cleanup_When_Destroy_Not_Configured()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        holder.Init(() => ValueTask.FromResult(new MyResource()));

        await holder.GetAsync();
        // Should not throw even without Destroy configured.
        await holder.DisposeAsync();
    }

    [Fact]
    public void Init_Returns_Same_Interface_For_Fluent_Chaining()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        var returned = holder.Init(() => ValueTask.FromResult(new MyResource()));

        Assert.Same(holder, returned);
    }

    [Fact]
    public void Destroy_Returns_Same_Interface_For_Fluent_Chaining()
    {
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        var returned = holder.Destroy(_ => ValueTask.CompletedTask);

        Assert.Same(holder, returned);
    }

    // -------------------------------------------------------------------------
    // Scoped DI registration
    // -------------------------------------------------------------------------

    [Fact]
    public void AddManagedActivationMemory_Is_Registered_As_Scoped()
    {
        // IManagedActivationMemory<T> must be scoped so each per-call IServiceScope
        // gets its own ManagedActivationMemoryAccessor (not a shared singleton).
        var services = new ServiceCollection();
        services.AddManagedActivationMemory<MyResource>();

        ServiceDescriptor descriptor = services.Single(d =>
            d.ServiceType == typeof(IManagedActivationMemory<MyResource>));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task Multiple_Accessors_For_Same_Holder_Share_State()
    {
        // Each per-call scope gets a distinct ManagedActivationMemoryAccessor wrapping
        // the same shell-owned ManagedActivationMemoryHolder. Verify that both accessors
        // observe the same initialized value (resource is created exactly once).
        var holder = new ManagedActivationMemoryHolder<MyResource>();
        var accessor1 = new ManagedActivationMemoryAccessor<MyResource>(holder);
        var accessor2 = new ManagedActivationMemoryAccessor<MyResource>(holder);

        accessor1.Init(() => ValueTask.FromResult(new MyResource()));

        MyResource r1 = await accessor1.GetAsync();
        MyResource r2 = await accessor2.GetAsync();

        Assert.Same(r1, r2);
        Assert.True(accessor2.IsInitialized);
    }

    private sealed class MyResource { }
}
