# Persistence in Quark

Quark provides a flexible persistence system that allows actors to store and retrieve state that survives restarts, failures, and even application redeployments.

## Core Concepts

### What is Actor Persistence?

Persistence in Quark allows actors to:
- **Save state** to durable storage (Redis, Postgres, etc.)
- **Load state** on activation
- **Recover** from failures without losing data
- **Migrate** between servers in a cluster

### Key Components

1. **`IStateStorage<T>`** - Interface for storage backends
2. **`IStateStorageProvider`** - Registry of storage backends
3. **`StatefulActorBase`** - Base class for actors with state
4. **`[QuarkState]`** - Attribute marking properties for persistence
5. **Source Generator** - Generates Load/Save/Delete methods at compile-time

## Quick Start

### Define a Stateful Actor

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Core.Persistence;

[Actor(Name = "UserProfile")]
public class UserProfileActor : StatefulActorBase
{
    // This property is automatically persisted
    [QuarkState]
    public UserProfile? Profile { get; set; }

    public UserProfileActor(
        string actorId,
        IActorFactory? actorFactory = null,
        IStateStorageProvider? stateStorageProvider = null)
        : base(actorId, actorFactory, stateStorageProvider)
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Load state automatically
        await base.OnActivateAsync(cancellationToken);

        if (Profile == null)
        {
            Profile = new UserProfile { UserId = ActorId };
        }
    }

    public void UpdateProfile(string name, string email)
    {
        Profile!.Name = name;
        Profile!.Email = email;
        Profile!.LastModified = DateTime.UtcNow;
    }
}

public class UserProfile
{
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime LastModified { get; set; }
}
```

### Use the Stateful Actor

```csharp
// Create storage provider
var storageProvider = new StateStorageProvider();
storageProvider.RegisterStorage(
    "default", 
    new InMemoryStateStorage<UserProfile>());

// Create actor factory with storage
var factory = new ActorFactory();

// Create actor
var actor = new UserProfileActor(
    "user:123", 
    factory, 
    storageProvider);

// Activate (loads state automatically)
await actor.OnActivateAsync();

// Modify state
actor.UpdateProfile("John Doe", "john@example.com");

// Save state
await actor.SaveStateAsync();

// Deactivate
await actor.OnDeactivateAsync();

// Later... create same actor again
var actor2 = new UserProfileActor(
    "user:123", 
    factory, 
    storageProvider);

// State is restored!
await actor2.OnActivateAsync();
Console.WriteLine($"Name: {actor2.Profile?.Name}"); // Output: Name: John Doe
```

## State Properties

### The `[QuarkState]` Attribute

Mark properties for automatic persistence:

```csharp
public class OrderActor : StatefulActorBase
{
    // Persisted properties
    [QuarkState]
    public Order? CurrentOrder { get; set; }

    [QuarkState]
    public OrderStatus? Status { get; set; }

    // Not persisted (transient)
    private DateTime _lastAccess;
    private int _cacheHits;

    // Not persisted (computed)
    public bool IsComplete => Status == OrderStatus.Completed;
}
```

### Multiple State Properties

You can have multiple persisted properties:

```csharp
public class ShoppingCartActor : StatefulActorBase
{
    [QuarkState]
    public List<CartItem>? Items { get; set; }

    [QuarkState]
    public CartMetadata? Metadata { get; set; }

    [QuarkState]
    public PaymentInfo? Payment { get; set; }
}
```

## State Operations

### Loading State

State is loaded automatically in `OnActivateAsync`:

```csharp
public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
{
    // Base class loads all [QuarkState] properties
    await base.OnActivateAsync(cancellationToken);

    // Initialize if no state exists
    if (Profile == null)
    {
        Profile = new UserProfile
        {
            UserId = ActorId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
```

### Saving State

Save state explicitly when you modify it:

```csharp
public async Task UpdateProfileAsync(string name, string email)
{
    // Modify state
    Profile!.Name = name;
    Profile!.Email = email;
    Profile!.LastModified = DateTime.UtcNow;

    // Persist changes
    await SaveStateAsync();
}
```

Or save in batches for better performance:

```csharp
public async Task ProcessBatchAsync(List<Update> updates)
{
    foreach (var update in updates)
    {
        // Modify state multiple times
        ApplyUpdate(update);
    }

    // Save once at the end
    await SaveStateAsync();
}
```

### Deleting State

Remove state from storage:

```csharp
public async Task DeleteAccountAsync()
{
    // Delete from storage
    await DeleteStateAsync();

    // Clear in-memory state
    Profile = null;
}
```

## Storage Backends

Quark supports multiple storage backends through the `IStateStorage<T>` interface.

### In-Memory Storage (Development)

Fast, but non-durable - lost on restart:

```csharp
var storage = new InMemoryStateStorage<UserProfile>();
var provider = new StateStorageProvider();
provider.RegisterStorage("default", storage);
```

### Redis Storage (Production)

Durable, distributed storage:

```csharp
using Quark.Storage.Redis;

var redis = ConnectionMultiplexer.Connect("localhost:6379");
var storage = new RedisStateStorage<UserProfile>(redis);

var provider = new StateStorageProvider();
provider.RegisterStorage("default", storage);
```

### PostgreSQL Storage (Production)

Relational database storage:

```csharp
using Quark.Storage.Postgres;

var connectionString = "Host=localhost;Database=quark;";
var storage = new PostgresStateStorage<UserProfile>(connectionString);

var provider = new StateStorageProvider();
provider.RegisterStorage("default", storage);
```

### Custom Storage

Implement `IStateStorage<T>` for custom backends:

```csharp
public class MyCustomStorage<T> : IStateStorage<T>
{
    public async Task<T?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        // Load from your storage
        return await MyDatabase.GetAsync<T>(key);
    }

    public async Task SaveAsync(string key, T state, CancellationToken cancellationToken = default)
    {
        // Save to your storage
        await MyDatabase.SetAsync(key, state);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        // Delete from your storage
        await MyDatabase.RemoveAsync(key);
    }
}
```

## Advanced Features

### Optimistic Concurrency

Prevent conflicting updates with ETags:

```csharp
public class StateWithVersion
{
    public string ETag { get; set; } = Guid.NewGuid().ToString();
    public UserProfile Data { get; set; } = new();
}

// Storage implementation checks ETag
public async Task SaveAsync(string key, StateWithVersion state, CancellationToken ct)
{
    var existing = await LoadAsync(key, ct);

    if (existing != null && existing.ETag != state.ETag)
    {
        throw new ConcurrencyException("State was modified by another instance");
    }

    // Update ETag on save
    state.ETag = Guid.NewGuid().ToString();
    await SaveToStorageAsync(key, state);
}
```

### Conditional Saves

Only save when state actually changed:

```csharp
public class UserActor : StatefulActorBase
{
    private string? _lastSavedVersion;

    [QuarkState]
    public UserProfile? Profile { get; set; }

    public async Task UpdateNameAsync(string name)
    {
        var currentVersion = ComputeHash(Profile);

        Profile!.Name = name;
        Profile!.LastModified = DateTime.UtcNow;

        var newVersion = ComputeHash(Profile);

        // Only save if changed
        if (currentVersion != newVersion)
        {
            await SaveStateAsync();
            _lastSavedVersion = newVersion;
        }
    }

    private string ComputeHash(object? obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
```

### Lazy Loading

Load state on demand rather than at activation:

```csharp
public class LazyActor : StatefulActorBase
{
    private bool _stateLoaded = false;

    [QuarkState]
    public LargeData? Data { get; set; }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Don't load state yet
        return Task.CompletedTask;
    }

    public async Task<LargeData> GetDataAsync()
    {
        if (!_stateLoaded)
        {
            await LoadStateAsync();
            _stateLoaded = true;
        }

        return Data ?? new LargeData();
    }
}
```

### Batched Writes

Accumulate changes and flush periodically:

```csharp
public class BatchedActor : StatefulActorBase
{
    private bool _isDirty = false;
    private Timer? _flushTimer;

    [QuarkState]
    public Counter? State { get; set; }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Flush every 5 seconds
        _flushTimer = new Timer(
            async _ => await FlushAsync(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));

        return base.OnActivateAsync(cancellationToken);
    }

    public void Increment()
    {
        State!.Count++;
        _isDirty = true; // Mark as needing flush
    }

    private async Task FlushAsync()
    {
        if (_isDirty)
        {
            await SaveStateAsync();
            _isDirty = false;
        }
    }

    public override async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        // Flush on deactivation
        await FlushAsync();
        _flushTimer?.Dispose();
        await base.OnDeactivateAsync(cancellationToken);
    }
}
```

## Source Generation

Quark's `StateSourceGenerator` automatically creates persistence code at compile-time.

### What Gets Generated

For this actor:

```csharp
public class MyActor : StatefulActorBase
{
    [QuarkState]
    public MyData? Data { get; set; }
}
```

The generator creates:

```csharp
// Generated code (simplified)
partial class MyActor
{
    protected override async Task LoadStateAsync(CancellationToken ct = default)
    {
        if (_stateStorageProvider == null) return;

        var storage = _stateStorageProvider.GetStorage<MyData>("default");
        Data = await storage.LoadAsync($"{ActorId}:Data", ct);
    }

    protected override async Task SaveStateAsync(CancellationToken ct = default)
    {
        if (_stateStorageProvider == null) return;

        var storage = _stateStorageProvider.GetStorage<MyData>("default");
        await storage.SaveAsync($"{ActorId}:Data", Data, ct);
    }

    protected override async Task DeleteStateAsync(CancellationToken ct = default)
    {
        if (_stateStorageProvider == null) return;

        var storage = _stateStorageProvider.GetStorage<MyData>("default");
        await storage.DeleteAsync($"{ActorId}:Data", ct);
    }
}
```

### No Reflection

All persistence code is generated at compile-time:
- ✅ Native AOT compatible
- ✅ Type-safe
- ✅ Zero runtime overhead
- ✅ Fully traceable

## Serialization

### JSON Serialization (Default)

Quark uses `System.Text.Json` with source generation:

```csharp
// Define a JSON context for AOT compatibility
[JsonSerializable(typeof(UserProfile))]
internal partial class MyJsonContext : JsonSerializerContext { }

// Use in storage
var options = new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default
};
```

### Custom Serialization

Implement custom serializers if needed:

```csharp
public class ProtobufStateStorage<T> : IStateStorage<T>
{
    public async Task SaveAsync(string key, T state, CancellationToken ct)
    {
        // Serialize with Protobuf
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, state);
        var bytes = stream.ToArray();

        await _storage.SetAsync(key, bytes);
    }

    // Similar for LoadAsync and DeleteAsync
}
```

## Best Practices

### 1. Keep State Small

Large state increases save/load time and storage costs:

```csharp
// ✅ Good: Small, focused state
public class OrderActor : StatefulActorBase
{
    [QuarkState]
    public OrderSummary? Summary { get; set; } // Just IDs and metadata
}

// ❌ Avoid: Large embedded data
public class OrderActor : StatefulActorBase
{
    [QuarkState]
    public OrderWithFullDetails? Order { get; set; } // Includes all line items, images, etc.
}
```

### 2. Save Strategically

Don't save on every change:

```csharp
// ✅ Good: Batch saves
public async Task ProcessOrderAsync(Order order)
{
    ValidateOrder(order);
    CalculateTotals(order);
    ApplyDiscounts(order);

    await SaveStateAsync(); // Save once at the end
}

// ❌ Avoid: Excessive saves
public async Task ProcessOrderAsync(Order order)
{
    ValidateOrder(order);
    await SaveStateAsync(); // Too many saves

    CalculateTotals(order);
    await SaveStateAsync();

    ApplyDiscounts(order);
    await SaveStateAsync();
}
```

### 3. Handle Missing State

Always check for null after loading:

```csharp
public override async Task OnActivateAsync(CancellationToken ct = default)
{
    await base.OnActivateAsync(ct);

    // Initialize if state doesn't exist
    if (Profile == null)
    {
        Profile = new UserProfile
        {
            UserId = ActorId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
```

### 4. Use Appropriate Storage

Choose storage based on requirements:

- **InMemory**: Development, testing, ephemeral data
- **Redis**: Fast, distributed, good for caches
- **Postgres**: ACID guarantees, relational queries, backups
- **Cosmos DB**: Global distribution, multi-model

### 5. Version Your State

Plan for schema evolution:

```csharp
public class VersionedState
{
    public int Version { get; set; } = 1;
    public UserProfile? Profile { get; set; }
}

public override async Task OnActivateAsync(CancellationToken ct = default)
{
    await base.OnActivateAsync(ct);

    if (State != null && State.Version < 2)
    {
        // Migrate from v1 to v2
        MigrateToV2(State);
        State.Version = 2;
        await SaveStateAsync();
    }
}
```

## Complete Example

Here's a full example of a stateful actor:

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Core.Persistence;
using System.Text.Json.Serialization;

// State model
public class BankAccountState
{
    public string AccountId { get; set; } = "";
    public decimal Balance { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
    public DateTime LastModified { get; set; }
}

public class Transaction
{
    public string Id { get; set; } = "";
    public decimal Amount { get; set; }
    public string Type { get; set; } = ""; // "deposit" or "withdraw"
    public DateTime Timestamp { get; set; }
}

// Actor
[Actor(Name = "BankAccount")]
public class BankAccountActor : StatefulActorBase
{
    [QuarkState]
    public BankAccountState? Account { get; set; }

    public BankAccountActor(
        string actorId,
        IActorFactory? actorFactory = null,
        IStateStorageProvider? stateStorageProvider = null)
        : base(actorId, actorFactory, stateStorageProvider)
    {
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        if (Account == null)
        {
            Account = new BankAccountState
            {
                AccountId = ActorId,
                Balance = 0,
                LastModified = DateTime.UtcNow
            };
            await SaveStateAsync();
        }
    }

    public async Task<bool> DepositAsync(decimal amount)
    {
        if (amount <= 0)
            return false;

        Account!.Balance += amount;
        Account.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid().ToString(),
            Amount = amount,
            Type = "deposit",
            Timestamp = DateTime.UtcNow
        });
        Account.LastModified = DateTime.UtcNow;

        await SaveStateAsync();
        return true;
    }

    public async Task<bool> WithdrawAsync(decimal amount)
    {
        if (amount <= 0 || Account!.Balance < amount)
            return false;

        Account.Balance -= amount;
        Account.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid().ToString(),
            Amount = amount,
            Type = "withdraw",
            Timestamp = DateTime.UtcNow
        });
        Account.LastModified = DateTime.UtcNow;

        await SaveStateAsync();
        return true;
    }

    public decimal GetBalance() => Account?.Balance ?? 0;

    public List<Transaction> GetTransactions() => Account?.Transactions ?? new();
}

// Usage
var provider = new StateStorageProvider();
provider.RegisterStorage("default", new InMemoryStateStorage<BankAccountState>());

var factory = new ActorFactory();
var account = new BankAccountActor("account:12345", factory, provider);

await account.OnActivateAsync();

await account.DepositAsync(100);
await account.DepositAsync(50);
await account.WithdrawAsync(30);

Console.WriteLine($"Balance: ${account.GetBalance()}"); // $120
Console.WriteLine($"Transactions: {account.GetTransactions().Count}"); // 3

// Actor can be deactivated and reactivated - state persists
await account.OnDeactivateAsync();

var account2 = new BankAccountActor("account:12345", factory, provider);
await account2.OnActivateAsync();
Console.WriteLine($"Balance after reload: ${account2.GetBalance()}"); // Still $120
```

## Next Steps

- **[Timers and Reminders](Timers-and-Reminders)** - Schedule periodic state saves
- **[Clustering](Clustering)** - Distribute stateful actors across machines
- **[Examples](Examples)** - See more persistence examples

---

**Related**: [Actor Model](Actor-Model) | [Source Generators](Source-Generators) | [API Reference](API-Reference)
