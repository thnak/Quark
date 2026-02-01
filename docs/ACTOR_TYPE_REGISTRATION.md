# Actor Type Registration and Interface Mapping Guide

## Overview

Quark now provides robust actor type registration with string-based lookup, enabling proper server-side dispatch of remote actor calls. This guide explains how actors are registered, how proxies identify target actors, and best practices for avoiding naming conflicts.

## Problem We Solved

### Before: Brittle Actor Type Identification

**Issues:**
1. **Simple Name Stripping**: Proxies used hardcoded logic to strip "I" prefix from interface names
   - `ICounterActor` → `"Counter"` (fragile, breaks with non-standard naming)
2. **No Namespace Validation**: Multiple actors with same simple name caused conflicts
   - `App1.ICounter` and `App2.ICounter` both became `"Counter"`
3. **No Server-Side Mapping**: Server couldn't resolve actor type names to actual classes
   - `ActorFactoryRegistry` only had `Type`-based lookup, not string-based
4. **Type Safety Issues**: Typos in actor names couldn't be detected at compile time

### After: Robust Type System

**Improvements:**
1. ✅ **Fully Qualified Names**: Proxies use complete interface names including namespace
2. ✅ **String → Type Mapping**: `ActorFactoryRegistry` maps actor names to types
3. ✅ **Explicit Interface Mapping**: `[Actor(InterfaceType = typeof(...))]` attribute
4. ✅ **Compile-Time Validation**: Duplicate actor names detected during code generation
5. ✅ **AOT-Safe**: All mappings use static dictionaries (no reflection)

## Actor Registration Flow

### 1. Actor Class Definition

```csharp
namespace MyApp.Actors;

// Option 1: Automatic registration (uses class name)
[Actor]
public class CounterActor : ActorBase, ICounterActor
{
    // Registers as "CounterActor"
}

// Option 2: Explicit name
[Actor(Name = "MyCounter")]
public class CounterActor : ActorBase, ICounterActor
{
    // Registers as "MyCounter"
}

// Option 3: Interface mapping (RECOMMENDED for proxies)
[Actor(InterfaceType = typeof(ICounterActor))]
public class CounterActor : ActorBase, ICounterActor
{
    // Registers as "MyApp.Actors.ICounterActor" (fully qualified)
}
```

### 2. Source Generator Registration

The `ActorSourceGenerator` processes `[Actor]` attributes and generates:

```csharp
// Generated ActorFactoryInitializer.g.cs
internal static class ActorFactoryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Priority: InterfaceType > Name > ClassName
        ActorFactoryRegistry.RegisterFactory<MyApp.Actors.CounterActor>(
            "MyApp.Actors.ICounterActor",  // Actor type name
            MyApp.Actors.CounterActorFactory.Create);
    }
}
```

### 3. Runtime Lookup

```csharp
// String → Type mapping
var actorType = ActorFactoryRegistry.GetActorType("MyApp.Actors.ICounterActor");
// Returns: typeof(CounterActor)

// Type → String mapping (reverse)
var actorName = ActorFactoryRegistry.GetActorTypeName(typeof(CounterActor));
// Returns: "MyApp.Actors.ICounterActor"
```

## Proxy Generation and Target Identification

### Client-Side Proxy Creation

```csharp
var client = serviceProvider.GetRequiredService<IClusterClient>();
var proxy = ActorProxyFactory.CreateProxy<ICounterActor>(client, "counter-1");

await proxy.IncrementAsync(1);
```

### Generated Proxy Code

```csharp
// Generated ICounterActorProxy.g.cs
internal sealed class ICounterActorProxy : ICounterActor
{
    public Task IncrementAsync(int amount)
    {
        // Uses FULLY QUALIFIED interface name for type safety
        var envelope = new QuarkEnvelope(
            messageId: Guid.NewGuid().ToString(),
            actorId: _actorId,
            actorType: "MyApp.Actors.ICounterActor",  // Full name!
            methodName: "IncrementAsync",
            payload: serializedParams);
            
        return _client.SendAsync(envelope, default);
    }
}
```

### Server-Side Dispatch (Conceptual)

```csharp
// Server receives envelope
var envelope = await _transport.ReceiveAsync();

// Resolve actor type from name
var actorType = ActorFactoryRegistry.GetActorType(envelope.ActorType);
// "MyApp.Actors.ICounterActor" → typeof(CounterActor)

// Create or get actor instance
var actor = actorFactory.GetOrCreateActor(actorType, envelope.ActorId);

// Invoke method on actor
await InvokeActorMethod(actor, envelope.MethodName, envelope.Payload);
```

## Best Practices

### ✅ DO: Use InterfaceType for Remote Actors

```csharp
// Client interface
public interface IOrderActor : IQuarkActor
{
    Task<Order> GetOrderAsync(string orderId);
    Task UpdateStatusAsync(OrderStatus status);
}

// Implementation with explicit interface mapping
[Actor(InterfaceType = typeof(IOrderActor))]
public class OrderActor : ActorBase, IOrderActor
{
    public OrderActor(string actorId) : base(actorId) { }
    
    public async Task<Order> GetOrderAsync(string orderId)
    {
        // Implementation
    }
    
    public async Task UpdateStatusAsync(OrderStatus status)
    {
        // Implementation
    }
}
```

**Why:**
- Ensures proxy calls use the correct actor type name
- Prevents namespace conflicts
- Makes intent explicit
- Enables type-safe server dispatch

### ✅ DO: Use Unique Names to Avoid Conflicts

```csharp
// If you have multiple implementations of the same interface
namespace MyApp.Redis
{
    [Actor(Name = "RedisCounterActor")]
    public class CounterActor : ActorBase, ICounterActor { }
}

namespace MyApp.Postgres
{
    [Actor(Name = "PostgresCounterActor")]
    public class CounterActor : ActorBase, ICounterActor { }
}
```

### ✅ DO: Group Related Actors by Namespace

```csharp
namespace MyApp.Orders
{
    [Actor(InterfaceType = typeof(IOrderActor))]
    public class OrderActor : ActorBase, IOrderActor { }
    
    [Actor(InterfaceType = typeof(IOrderProcessorActor))]
    public class OrderProcessorActor : ActorBase, IOrderProcessorActor { }
}
```

### ❌ DON'T: Rely on Simple Name Stripping

```csharp
// Old pattern (no longer used)
// Proxy sent: actorType: "Counter" (stripped "I" prefix)
// Server can't find "Counter" if registered as "CounterActor"

// New pattern (automatic)
// Proxy sends: actorType: "MyApp.ICounterActor" (full name)
// Server resolves via ActorFactoryRegistry
```

### ❌ DON'T: Register Multiple Actors with Same Name

```csharp
// This will throw at startup!
[Actor(Name = "Counter")]
public class CounterActorV1 : ActorBase { }

[Actor(Name = "Counter")]  // ERROR: Duplicate name!
public class CounterActorV2 : ActorBase { }

// Error message:
// InvalidOperationException: Actor type name 'Counter' is already registered 
// for type 'CounterActorV1'. Cannot register it again for type 'CounterActorV2'.
```

## Advanced Scenarios

### Multiple Interfaces, One Implementation

```csharp
public interface IReadOnlyOrderActor : IQuarkActor
{
    Task<Order> GetOrderAsync(string orderId);
}

public interface IOrderActor : IReadOnlyOrderActor
{
    Task UpdateStatusAsync(OrderStatus status);
}

// Register under the main interface
[Actor(InterfaceType = typeof(IOrderActor))]
public class OrderActor : ActorBase, IOrderActor, IReadOnlyOrderActor
{
    // Implementation
}

// Clients can use the main interface
var fullProxy = ActorProxyFactory.CreateProxy<IOrderActor>(client, "order-1");
// Sends actorType: "MyApp.IOrderActor"

// Note: IReadOnlyOrderActor proxy would send "MyApp.IReadOnlyOrderActor"
// which won't resolve unless registered separately. For this pattern,
// clients should use the registered interface type (IOrderActor) only.
```

### Versioned Actors

```csharp
// V1 interface
public interface IOrderActorV1 : IQuarkActor
{
    Task<Order> GetOrderAsync(string orderId);
}

// V2 interface (new methods)
public interface IOrderActorV2 : IQuarkActor
{
    Task<Order> GetOrderAsync(string orderId);
    Task<decimal> GetTotalAsync();  // New in V2
}

// V1 implementation
[Actor(InterfaceType = typeof(IOrderActorV1))]
public class OrderActorV1 : ActorBase, IOrderActorV1 { }

// V2 implementation (separate actor)
[Actor(InterfaceType = typeof(IOrderActorV2))]
public class OrderActorV2 : ActorBase, IOrderActorV2 { }

// Clients choose version - IMPORTANT: Use different actor IDs!
var v1Proxy = ActorProxyFactory.CreateProxy<IOrderActorV1>(client, "order-v1-123");
var v2Proxy = ActorProxyFactory.CreateProxy<IOrderActorV2>(client, "order-v2-123");

// Note on Actor ID Versioning:
// When running multiple versions of the same logical actor, use versioned actor IDs:
// - Option 1: Include version in ID: "order-v1-123", "order-v2-123"
// - Option 2: Use separate namespaces: "v1/order-123", "v2/order-123"
// - Option 3: Route based on feature flags or migration state
// This prevents ID collisions and allows gradual migration between versions.
```

## Debugging Actor Type Issues

### Check Registered Actor Types

```csharp
// Get all registered actors
var allActors = ActorFactoryRegistry.GetAllActorTypes();

foreach (var (actorTypeName, actorType) in allActors)
{
    Console.WriteLine($"{actorTypeName} → {actorType.FullName}");
}

// Example output:
// MyApp.ICounterActor → MyApp.Actors.CounterActor
// MyApp.IOrderActor → MyApp.Actors.OrderActor
```

### Verify Actor Type Resolution

```csharp
// Check if actor type name resolves
var actorTypeName = "MyApp.ICounterActor";
var actorType = ActorFactoryRegistry.GetActorType(actorTypeName);

if (actorType == null)
{
    Console.WriteLine($"ERROR: No actor registered for type name '{actorTypeName}'");
}
else
{
    Console.WriteLine($"✓ Resolved '{actorTypeName}' → {actorType.FullName}");
}
```

### Common Error Messages

**Duplicate Actor Name:**
```
InvalidOperationException: Actor type name 'Counter' is already registered 
for type 'MyApp.Actors.CounterActorV1'. 
Cannot register it again for type 'MyApp.Actors.CounterActorV2'. 
Use [Actor(Name = "UniqueName")] attribute to specify a unique name.
```

**Actor Not Found:**
```
InvalidOperationException: No factory registered for actor type CounterActor. 
Ensure the actor is marked with [Actor] attribute for source generation.
```

## Migration Guide

### Updating Existing Actors

If you have existing actors without explicit interface mapping:

```csharp
// Before (automatic, class name used)
[Actor]
public class CounterActor : ActorBase, ICounterActor
{
    // Registered as "CounterActor"
}

// After (explicit interface mapping)
[Actor(InterfaceType = typeof(ICounterActor))]
public class CounterActor : ActorBase, ICounterActor
{
    // Registered as "MyNamespace.ICounterActor"
}
```

**Impact:**
- Clients using `ActorProxyFactory.CreateProxy<ICounterActor>()` will automatically use the new fully qualified name
- Tests expecting simple names (e.g., `"Counter"`) need to be updated to expect fully qualified names (e.g., `"MyNamespace.ICounterActor"`)

### Updating Tests

```csharp
// Before
Assert.Equal("Counter", envelope.ActorType);

// After
Assert.Equal("MyNamespace.ICounterActor", envelope.ActorType);
```

## Performance Considerations

### Lookup Performance

- **String → Type**: O(1) dictionary lookup (very fast)
- **Type → String**: O(1) dictionary lookup (very fast)
- **Memory**: Static dictionaries initialized once at startup (minimal overhead)
- **AOT Impact**: Zero reflection, fully compatible with Native AOT

### Registration Performance

- **Compile-Time**: Source generator runs once during build
- **Startup**: Module initializer runs once when assembly loads
- **No Runtime Cost**: All mappings pre-computed

## Summary

✅ **Actors now have robust type registration** with string-based lookup  
✅ **Proxies use fully qualified interface names** for type safety  
✅ **Explicit interface mapping** via `[Actor(InterfaceType = typeof(...))]`  
✅ **Compile-time validation** prevents duplicate actor names  
✅ **AOT-safe** with static dictionaries and zero reflection  
✅ **Server-side dispatch** can resolve actor types from envelope names

Use `[Actor(InterfaceType = typeof(IMyActor))]` for all actors that are called remotely via proxies!
