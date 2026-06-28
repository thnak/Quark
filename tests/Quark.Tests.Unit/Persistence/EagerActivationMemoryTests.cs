using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Hosting;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Persistence;

public sealed class EagerActivationMemoryTests
{
    // -------------------------------------------------------------------------
    // InitAsync — factory invocation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InitAsync_Calls_Factory_Once()
    {
        int callCount = 0;
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => { callCount++; return ValueTask.FromResult(new MyEagerResource()); });

        await holder.InitAsync(new ServiceCollection().BuildServiceProvider(), CancellationToken.None);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task InitAsync_Is_Idempotent_On_Subsequent_Calls()
    {
        int callCount = 0;
        var resource = new MyEagerResource();
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => { callCount++; return ValueTask.FromResult(resource); });

        var sp = new ServiceCollection().BuildServiceProvider();
        await holder.InitAsync(sp, CancellationToken.None);
        await holder.InitAsync(sp, CancellationToken.None);
        await holder.InitAsync(sp, CancellationToken.None);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task InitAsync_Throws_If_Load_Never_Called()
    {
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => holder.InitAsync(new ServiceCollection().BuildServiceProvider(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task InitAsync_Passes_ScopedServiceProvider_To_Factory()
    {
        string? resolvedId = null;
        var services = new ServiceCollection();
        services.AddScoped<MyScopedService>();
        var sp = services.BuildServiceProvider();

        using IServiceScope scope = sp.CreateScope();
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((factorySp, _) =>
        {
            resolvedId = factorySp.GetRequiredService<MyScopedService>().Id;
            return ValueTask.FromResult(new MyEagerResource());
        });

        await holder.InitAsync(scope.ServiceProvider, CancellationToken.None);

        Assert.NotNull(resolvedId);
        Assert.NotEmpty(resolvedId);
    }

    // -------------------------------------------------------------------------
    // Value — synchronous access
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Value_Returns_Initialized_Resource()
    {
        var resource = new MyEagerResource();
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => ValueTask.FromResult(resource));

        await holder.InitAsync(new ServiceCollection().BuildServiceProvider(), CancellationToken.None);

        Assert.Same(resource, holder.Value);
    }

    [Fact]
    public void Value_Throws_Before_InitAsync_Is_Called()
    {
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => ValueTask.FromResult(new MyEagerResource()));

        Assert.Throws<InvalidOperationException>(() => _ = holder.Value);
    }

    [Fact]
    public void Value_Throws_When_Load_Was_Never_Called()
    {
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();

        Assert.Throws<InvalidOperationException>(() => _ = holder.Value);
    }

    // -------------------------------------------------------------------------
    // IsInitialized
    // -------------------------------------------------------------------------

    [Fact]
    public void IsInitialized_Returns_False_Before_InitAsync()
    {
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => ValueTask.FromResult(new MyEagerResource()));

        Assert.False(holder.IsInitialized);
    }

    [Fact]
    public async Task IsInitialized_Returns_True_After_InitAsync()
    {
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => ValueTask.FromResult(new MyEagerResource()));

        await holder.InitAsync(new ServiceCollection().BuildServiceProvider(), CancellationToken.None);

        Assert.True(holder.IsInitialized);
    }

    // -------------------------------------------------------------------------
    // DisposeAsync — cleanup delegate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_Calls_Cleanup_When_Initialized()
    {
        bool cleanupCalled = false;
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => ValueTask.FromResult(new MyEagerResource()))
              .Destroy(_ => { cleanupCalled = true; return ValueTask.CompletedTask; });

        await holder.InitAsync(new ServiceCollection().BuildServiceProvider(), CancellationToken.None);
        await holder.DisposeAsync();

        Assert.True(cleanupCalled);
    }

    [Fact]
    public async Task DisposeAsync_Passes_Value_To_Cleanup()
    {
        var resource = new MyEagerResource();
        MyEagerResource? received = null;
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => ValueTask.FromResult(resource))
              .Destroy(r => { received = r; return ValueTask.CompletedTask; });

        await holder.InitAsync(new ServiceCollection().BuildServiceProvider(), CancellationToken.None);
        await holder.DisposeAsync();

        Assert.Same(resource, received);
    }

    [Fact]
    public async Task DisposeAsync_Skips_Cleanup_If_Not_Initialized()
    {
        bool cleanupCalled = false;
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => ValueTask.FromResult(new MyEagerResource()))
              .Destroy(_ => { cleanupCalled = true; return ValueTask.CompletedTask; });

        await holder.DisposeAsync();

        Assert.False(cleanupCalled);
    }

    [Fact]
    public async Task DisposeAsync_Does_Not_Throw_When_Destroy_Not_Configured()
    {
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        holder.Load((_, _) => ValueTask.FromResult(new MyEagerResource()));

        await holder.InitAsync(new ServiceCollection().BuildServiceProvider(), CancellationToken.None);
        await holder.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Fluent chaining
    // -------------------------------------------------------------------------

    [Fact]
    public void Load_Returns_Same_Interface_For_Fluent_Chaining()
    {
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        var returned = holder.Load((_, _) => ValueTask.FromResult(new MyEagerResource()));

        Assert.Same(holder, returned);
    }

    [Fact]
    public void Destroy_Returns_Same_Interface_For_Fluent_Chaining()
    {
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        var returned = holder.Destroy(_ => ValueTask.CompletedTask);

        Assert.Same(holder, returned);
    }

    // -------------------------------------------------------------------------
    // Scoped DI registration
    // -------------------------------------------------------------------------

    [Fact]
    public void AddEagerActivationMemory_Is_Registered_As_Scoped()
    {
        var services = new ServiceCollection();
        services.AddEagerActivationMemory<MyEagerResource>();

        ServiceDescriptor descriptor = services.Single(d =>
            d.ServiceType == typeof(IEagerActivationMemory<MyEagerResource>));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task Multiple_Accessors_For_Same_Holder_Share_State()
    {
        var resource = new MyEagerResource();
        var holder = new EagerActivationMemoryHolder<MyEagerResource>();
        var accessor1 = new EagerActivationMemoryAccessor<MyEagerResource>(holder);
        var accessor2 = new EagerActivationMemoryAccessor<MyEagerResource>(holder);

        accessor1.Load((_, _) => ValueTask.FromResult(resource));
        await holder.InitAsync(new ServiceCollection().BuildServiceProvider(), CancellationToken.None);

        Assert.Same(resource, accessor1.Value);
        Assert.Same(resource, accessor2.Value);
        Assert.True(accessor2.IsInitialized);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed class MyEagerResource { }

    private sealed class MyScopedService
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    }
}
