# Persistence

Quark offers four persistence patterns, from ephemeral in-memory state to full event sourcing.

## 1. In-memory activation state (`IActivationMemory<T>`)

For state that must survive across method calls on the same activation but does **not** need to outlive it:

```csharp
public sealed class CounterState { public int Count { get; set; } }

public sealed class CounterBehavior : IGrainBehavior, ICounterGrain
{
    private readonly IActivationMemory<CounterState> _memory;

    public CounterBehavior(IActivationMemory<CounterState> memory)
        => _memory = memory;

    public Task IncrementAsync() { _memory.Value.Count++; return Task.CompletedTask; }
    public Task<int> GetAsync()  => Task.FromResult(_memory.Value.Count);
}
```

Register in silo startup:

```csharp
silo.Services.AddScoped<IActivationMemory<CounterState>>(sp =>
    new ActivationMemoryAccessor<CounterState>(
        sp.GetRequiredService<IActivationShellAccessor>()
          .Shell.GetOrCreateHolder<CounterState>()));
```

The `StateHolder<TState>` lives on the `GrainActivation` shell and is shared across all per-call scopes for the same activation.

## 2. Persistent activation state (`IPersistentActivationMemory<T>`)

Drop-in for `IActivationMemory<T>` with automatic load-on-first-access and explicit `WriteAsync()`:

```csharp
public sealed class AccountBehavior : IGrainBehavior, IAccountGrain, IActivationLifecycle
{
    private readonly IPersistentActivationMemory<AccountState> _state;

    public AccountBehavior(IPersistentActivationMemory<AccountState> state)
        => _state = state;

    public async Task OnActivateAsync(CancellationToken ct)
        => await _state.ReadStateAsync(ct);

    public async Task DepositAsync(decimal amount)
    {
        _state.Value.Balance += amount;
        await _state.WriteStateAsync();
    }

    public Task<decimal> GetBalanceAsync()
        => Task.FromResult(_state.Value.Balance);
}
```

Register in silo startup (replace `InMemory` with `Redis` for durable storage):

```csharp
silo.Services.AddInMemoryGrainStorage();

silo.Services.AddScoped<IPersistentActivationMemory<AccountState>>(sp =>
    new PersistentActivationMemoryAccessor<AccountState>(
        sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateHolder<AccountState>(),
        sp.GetRequiredService<IGrainStorage>(),
        sp.GetRequiredService<IActivationShellAccessor>().Shell.GrainId,
        "accounts")); // storage provider name
```

## 3. `[PersistentState]` attribute injection (Orleans-compatible)

Grains that want Orleans-style named state injection use `IPersistentState<T>` with the `[PersistentState]` attribute:

```csharp
public sealed class ProfileBehavior : IGrainBehavior, IProfileGrain
{
    private readonly IPersistentState<ProfileData> _profile;

    public ProfileBehavior(
        [PersistentState("profile", "profileStore")] IPersistentState<ProfileData> profile)
    {
        _profile = profile;
    }

    public async Task UpdateNameAsync(string name)
    {
        _profile.State.Name = name;
        await _profile.WriteStateAsync();
    }
}
```

Register the named storage provider:

```csharp
services.AddInMemoryGrainStorage("profileStore");
// or
services.AddRedisGrainStorage("profileStore", opt => opt.ConnectionString = "localhost:6379");
```

The `[PersistentState("name","provider")]` attribute is resolved at construction time — no reflection at call time.

## 4. Event sourcing (`JournaledGrain<TState, TEvent>`)

For grains whose history of decisions matters. Inherit from `JournaledGrain<TState,TEvent>`:

```csharp
public sealed class BankAccountState { public decimal Balance { get; set; } }
public abstract record AccountEvent;
public record Deposited(decimal Amount) : AccountEvent;
public record Withdrawn(decimal Amount) : AccountEvent;

public sealed class BankAccountBehavior
    : JournaledGrain<BankAccountState, AccountEvent>, IBankAccountGrain
{
    public BankAccountBehavior(
        IActivationMemory<JournaledGrainState<BankAccountState, AccountEvent>> memory,
        ICallContext ctx,
        ILogStorage logStorage)
        : base(memory, ctx, logStorage) { }

    protected override void TransitionState(BankAccountState state, AccountEvent @event)
    {
        switch (@event)
        {
            case Deposited(var amount):  state.Balance += amount; break;
            case Withdrawn(var amount):  state.Balance -= amount; break;
        }
    }

    public async Task DepositAsync(decimal amount)
    {
        RaiseEvent(new Deposited(amount));
        await ConfirmEventsAsync();
    }

    public Task<decimal> GetBalanceAsync() => Task.FromResult(State.Balance);
}
```

Key members:
- `RaiseEvent(event)` — applies the event to `State` immediately; stages it for persistence
- `ConfirmEventsAsync()` — persists all staged events to `ILogStorage`
- `RetrieveConfirmedEvents(from, to)` — reads events back from storage
- `Version` — number of confirmed events
- `OnActivateAsync` — automatically replays the event log on first activation

Register an `ILogStorage` provider (in-memory is provided):

```csharp
services.AddInMemoryLogStorage();
```

## Storage providers

### In-memory

```csharp
services.AddInMemoryGrainStorage();               // default provider
services.AddInMemoryGrainStorage("myProvider");   // named provider
```

### Redis

```csharp
services.AddRedisGrainStorage(options =>
{
    options.ConnectionString = "localhost:6379";
});

services.AddRedisGrainStorage("myProvider", options =>
{
    options.ConnectionString = "redis.internal:6379";
    options.KeyPrefix = "quark:";
});
```

State is serialized to JSON via `System.Text.Json` and stored as a Redis string under key `{prefix}{grainType}/{grainId}/{stateName}`.

## Idle-timeout deactivation

Grains that haven't received a call within the configured idle period are deactivated by `GrainIdleCollector`. Configure via `SiloRuntimeOptions`:

```csharp
silo.Services.Configure<SiloRuntimeOptions>(opts =>
{
    opts.CollectionAge = TimeSpan.FromMinutes(5);
    opts.CollectionInterval = TimeSpan.FromMinutes(1);
});
```

To prevent deactivation of a specific grain, call `DelayDeactivation(TimeSpan)` from inside the behavior:

```csharp
_ctx.DelayDeactivation(TimeSpan.FromHours(1));
```
