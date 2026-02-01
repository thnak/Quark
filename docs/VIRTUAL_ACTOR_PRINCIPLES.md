# Virtual Actor Principles in Quark Framework

## Overview

Quark is a **Virtual Actor framework** similar to Microsoft Orleans. This document explains the critical principles that prevent reference leaks and ensure proper distributed actor behavior.

## Core Principle: Never Direct References

**In a Virtual Actor framework, clients MUST NEVER get direct references to real actor instances.**

### Why This Matters

1. **Location Transparency**: Actors can be anywhere in the cluster. Direct references break this abstraction.
2. **Serialization Boundary**: All actor calls must cross a serialization boundary to support network communication.
3. **Reference Leaks**: Direct references prevent actors from being deactivated/migrated properly.
4. **State Consistency**: Direct references bypass the single-threaded actor guarantee.

### The Problem with Direct References

```csharp
// ❌ WRONG: Getting a direct actor reference (reference leak!)
var actor = actorFactory.GetOrCreateActor<MyActor>("actor-1");
await actor.DoSomethingAsync(); // This bypasses the transport layer!
```

When you have a direct reference:
- Calls execute in-process, bypassing serialization
- Actor cannot be deactivated while you hold the reference
- Actor cannot be migrated to another silo
- Breaks distributed system semantics

## The Correct Pattern: Proxies

### Client-Side Usage

Clients should **always** use lightweight proxies that route through the cluster client:

```csharp
// ✅ CORRECT: Get a proxy that routes through transport
var client = serviceProvider.GetRequiredService<IClusterClient>();
var proxy = ActorProxyFactory.CreateProxy<IMyActor>(client, "actor-1");
await proxy.DoSomethingAsync(); // Routes through transport layer
```

### How It Works

1. `ActorProxyFactory.CreateProxy<T>()` is generated at compile-time by `ProxySourceGenerator`
2. The proxy implements your actor interface
3. Each method call:
   - Serializes parameters to Protobuf
   - Creates a `QuarkEnvelope` 
   - Calls `IClusterClient.SendAsync(envelope)`
   - Routes to the correct silo via consistent hashing
   - Deserializes the response

### Proxy Generation

Proxies are generated for any interface that:
1. Inherits from `IQuarkActor`
2. Is registered via `[QuarkActorContext]` attribute

Example:

```csharp
// Define your actor interface
public interface ICounterActor : IQuarkActor
{
    Task IncrementAsync(int amount);
    Task<int> GetValueAsync();
}

// ProxySourceGenerator creates:
// - ICounterActorProxy class (implements ICounterActor)
// - Protobuf message contracts
// - ActorProxyFactory.CreateProxy<ICounterActor>() method
```

## Server-Side Usage (Silos)

On the server side (silos), actors are **real instances**:

```csharp
// In a silo, actors are created by ActorFactory
var factory = serviceProvider.GetRequiredService<IActorFactory>();
var actor = factory.CreateActor<CounterActor>("counter-1");
await actor.OnActivateAsync(); // Real actor activation
```

### Local Calls on Silo

Even when calling actors on the same silo, you have two options:

1. **Through Transport** (Recommended): Use proxies even for local calls
   ```csharp
   var proxy = ActorProxyFactory.CreateProxy<ICounterActor>(client, "counter-1");
   await proxy.IncrementAsync(1); // Goes through transport (optimized for local)
   ```

2. **Direct Call** (Only in specific scenarios): Use `IActorFactory` for direct parent-child relationships
   ```csharp
   // Only in ActorBase.SpawnChildAsync where parent manages child lifecycle
   var child = _actorFactory.CreateActor<ChildActor>("child-1");
   ```

### When Direct References Are Acceptable

Direct references are **only** safe in these scenarios:

1. **Parent-Child Supervision**: `ActorBase.SpawnChildAsync<T>()` creates direct child references
   - Parent owns the child's lifecycle
   - Child is deactivated when parent is deactivated
   - No cross-silo references

2. **Internal Actor Implementation**: Inside an actor's own methods
   - Actor can call its own methods directly
   - No external references involved

## ClusterClient.GetActor() Deprecation

### Current Status

`IClusterClient.GetActor<T>()` throws `NotImplementedException` with a helpful error message:

```csharp
// ❌ This will throw NotImplementedException
var actor = client.GetActor<IMyActor>("actor-1");
```

Error message:
```
ClusterClient.GetActor<IMyActor>() is not directly implemented.
Use ActorProxyFactory.CreateProxy<IMyActor>(clusterClient, actorId) instead.
This method is generated at compile-time by the ProxySourceGenerator.
```

### Why This Design?

1. **Per-Assembly Generation**: `ActorProxyFactory.CreateProxy<T>()` is generated **per consuming assembly**
2. **Quark.Client Library**: Does not contain any actor interfaces, so no `CreateProxy` method exists there
3. **Compile-Time Safety**: Each consumer gets type-safe, AOT-compatible proxies for their interfaces
4. **Zero Reflection**: Completely reflection-free, works with Native AOT

### Migration Guide

If you were using `client.GetActor<T>()`, change to:

```csharp
// Before (won't work)
var actor = client.GetActor<IMyActor>("actor-1");

// After (correct)
var actor = ActorProxyFactory.CreateProxy<IMyActor>(client, "actor-1");
```

## Architecture Diagrams

### Wrong: Direct Reference Leak

```
┌─────────┐
│ Client  │
└────┬────┘
     │ GetOrCreateActor<T>()
     ├─────────────────────────────┐
     │                             │
     ▼                             ▼
┌──────────┐                 ┌──────────┐
│ Silo A   │                 │ Silo B   │
│ Actor-1  │◄────────────────│  (empty) │
└──────────┘                 └──────────┘
     ▲
     │ PROBLEM: Direct reference!
     │ Actor-1 can't migrate to Silo B
     │ Client bypasses serialization
```

### Correct: Proxy Pattern

```
┌─────────┐
│ Client  │
│ Proxy   │◄── CreateProxy<T>(client, id)
└────┬────┘
     │ IClusterClient.SendAsync()
     │ (Protobuf serialized)
     ├─────────────────────────────┐
     │                             │
     ▼                             ▼
┌──────────┐                 ┌──────────┐
│ Silo A   │                 │ Silo B   │
│ Actor-1  │                 │  (ready) │
└──────────┘                 └──────────┘
     ▲
     │ ✓ Location transparent
     │ ✓ Can migrate freely
     │ ✓ Serialization boundary enforced
```

## Summary Checklist

- ✅ Use `ActorProxyFactory.CreateProxy<T>()` for all client-side actor calls
- ✅ Proxies are lightweight, created per-call (no caching needed)
- ✅ All calls go through `IClusterClient.SendAsync()` with serialization
- ✅ Actors can be freely deactivated and migrated
- ❌ Never use `IActorFactory` on the client side
- ❌ Never cache direct actor references outside parent-child relationships
- ❌ Don't call `GetOrCreateActor<T>()` from clients

## References

- [Orleans Virtual Actor Model](https://learn.microsoft.com/en-us/dotnet/orleans/overview)
- [Quark Type-Safe Proxies](TYPE_SAFE_PROXIES.md)
- [Quark Zero Reflection Achievement](ZERO_REFLECTION_ACHIEVEMENT.md)
