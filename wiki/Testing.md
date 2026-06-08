# Testing

Quark ships a `Quark.Testing` package with an in-process test harness: `TestCluster`. It spins up one or more real `SiloHostedService` instances and a matching `LocalClusterClient` in the same process, giving full integration coverage without any network overhead.

## Basic setup

```csharp
await using var cluster = await TestCluster.CreateAsync(options =>
{
    options.ConfigureSiloServices = services =>
    {
        services.AddGrainBehavior<ICounterGrain, CounterBehavior>();
        services.AddScoped<IActivationMemory<CounterState>>(sp =>
            new ActivationMemoryAccessor<CounterState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<CounterState>()));
    };
    options.ConfigureClientServices = services =>
    {
        services.AddGrainProxy<ICounterGrain, CounterGrainProxy>();
    };
});

var grain = cluster.Client.GetGrain<ICounterGrain>("test");
await grain.IncrementAsync();
Assert.Equal(1, await grain.GetAsync());
```

In test projects, write method invokers and proxies by hand (see `tests/Quark.Tests.Unit/Integration/`). Hand-written invokers are simpler than running the code generator in test projects.

## xUnit pattern

```csharp
public sealed class CounterGrainTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        _cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
                services.AddGrainBehavior<ICounterGrain, CounterBehavior>()
                        .AddInMemoryGrainStorage();
            options.ConfigureClientServices = services =>
                services.AddGrainProxy<ICounterGrain, CounterGrainProxy>();
        });
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.DisposeAsync();
    }

    [Fact]
    public async Task Increment_persists_across_calls()
    {
        var grain = _cluster!.Client.GetGrain<ICounterGrain>("x");
        await grain.IncrementAsync();
        await grain.IncrementAsync();
        Assert.Equal(2, await grain.GetAsync());
    }
}
```

## Testing persistence

Swap in the in-memory storage provider — no containers needed for unit tests:

```csharp
options.ConfigureSiloServices = services =>
{
    services.AddGrainBehavior<IProfileGrain, ProfileBehavior>();
    services.AddInMemoryGrainStorage("profileStore");
};
```

## Testing reminders

```csharp
options.ConfigureSiloServices = services =>
{
    services.AddGrainBehavior<ISubscriptionGrain, SubscriptionBehavior>();
    services.AddInMemoryReminderService();
};
```

Reminders fire based on wall-clock time; for unit tests, keep periods short or call `ReceiveReminder` directly.

## Testing streams

```csharp
options.ConfigureSiloServices = services =>
{
    services.AddMemoryStreams("events");
    services.AddStreamableCodec<MyEvent, MyEventCodec>();
    services.AddGrainBehavior<IProcessorGrain, ProcessorBehavior>();
};
```

## Integration tests with Redis (Testcontainers)

Tests in `Quark.Tests.Integration` that require Redis use [Testcontainers](https://dotnet.testcontainers.org/):

```csharp
[Trait("category", "integration")]
public sealed class RedisPersistenceTests : IAsyncLifetime
{
    private RedisContainer _redis = null!;
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().Build();
        await _redis.StartAsync();

        _cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddGrainBehavior<IAccountGrain, AccountBehavior>();
                services.AddRedisGrainStorage(opts =>
                    opts.ConnectionString = _redis.GetConnectionString());
            };
        });
    }

    public async Task DisposeAsync()
    {
        await _cluster.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
```

Skip integration tests in CI environments where infrastructure is unavailable by filtering on the `[Trait("category","integration")]` attribute:

```bash
dotnet test --filter "category!=integration"
```

## Fault tests

`Quark.Tests.Fault` tests cluster behavior under adverse conditions — grain crashes, silo failures, message loss. See `tests/Quark.Tests.Fault/` for examples using `FaultFixture` and `FaultScenario`.

## Running tests

```bash
# All tests
dotnet test Quark.slnx

# Unit tests only
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj

# Code generator tests
dotnet test tests/Quark.Tests.CodeGenerator/Quark.Tests.CodeGenerator.csproj

# Integration tests (requires Docker for Redis)
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj

# Filter by name
dotnet test --filter "FullyQualifiedName~GrainCallIntegrationTests"
```
