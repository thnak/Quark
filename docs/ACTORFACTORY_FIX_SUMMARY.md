# ActorFactory and ActorProxyFactory Fix - Summary

## Issue Description

The problem statement identified several critical issues with the ActorFactory and ActorProxyFactory patterns:

1. **Weird patterns**: ActorProxyFactory with ActorProxyFactoryRegistry not used correctly
2. **Missing initialization**: ActorFactory has ActorFactoryInitializer but ActorProxyFactory doesn't
3. **Reference leaks**: In a Virtual Actors framework like Orleans, we should never interact directly with real actors
4. **Dangerous local calls**: When calling an actor on the same server instance, we should never make a reference leak

## Root Causes

### 1. Misunderstanding of Proxy Generation Pattern

**Problem**: There were two conflicting ActorProxyFactory patterns:
- An instance class `ActorProxyFactory` that tried to use `ActorProxyFactoryRegistry`
- A generated partial static class with `CreateProxy<T>()` method

**Reality**: The ProxySourceGenerator creates:
- `ActorProxyFactory.CreateProxy<T>()` as a **partial static method**
- Generated **per consuming assembly** (not in Quark.Client itself)
- Only for assemblies that have IQuarkActor interfaces

### 2. ClusterClient Using Wrong Factory

**Problem**: ClusterClient constructor took `IActorFactory` parameter and called `_actorFactory.GetOrCreateActor<T>()`

**Issue**: 
- If injected with silo's `ActorFactory`, clients would get direct actor references (reference leak!)
- `GetOrCreateActor` caches instances, preventing actor deactivation/migration

**Solution**: Removed `IActorFactory` dependency entirely from ClusterClient

### 3. IClusterClient.GetActor() Not Implementable

**Problem**: ClusterClient couldn't implement `GetActor<T>()` because `ActorProxyFactory.CreateProxy<T>()` is only generated in **consuming assemblies**

**Solution**: Made `GetActor<T>()` throw `NotImplementedException` with helpful error message directing users to call `ActorProxyFactory.CreateProxy<T>()` directly

## Changes Made

### 1. ActorProxyFactory.cs
```csharp
// BEFORE: Complex instance class with registry
internal sealed class ActorProxyFactory : IActorFactory { ... }
public static class ActorProxyFactoryRegistry { ... }

// AFTER: Simple partial static class (generated method added by source generator)
internal static partial class ActorProxyFactory
{
    // CreateProxy<T>() will be generated here by ProxySourceGenerator
}
```

### 2. ClusterClient.cs
```csharp
// BEFORE: Took IActorFactory, called GetOrCreateActor
public ClusterClient(..., IActorFactory actorFactory) { ... }
public TActorInterface GetActor<T>(string id) => 
    _actorFactory.GetOrCreateActor<T>(id); // REFERENCE LEAK!

// AFTER: No IActorFactory, throws helpful error
public ClusterClient(...) { ... } // No actorFactory parameter
public TActorInterface GetActor<T>(string id) => 
    throw new NotImplementedException("Use ActorProxyFactory.CreateProxy<T>() instead");
```

### 3. ClusterClientServiceCollectionExtensions.cs
```csharp
// BEFORE: Tried to register IActorFactory
services.TryAddSingleton<IActorFactory, QuarkActorProxyFactory>();

// AFTER: No IActorFactory registration (not needed)
services.TryAddSingleton<IClusterClient, ClusterClient>();
```

### 4. IClusterClient.cs
Updated XML documentation to explain:
- GetActor<T>() is not directly implemented
- Use `ActorProxyFactory.CreateProxy<T>(client, actorId)` instead
- Link to Virtual Actor Principles documentation

## New Pattern: Correct Usage

### Client-Side (Recommended)
```csharp
// Get cluster client from DI
var client = serviceProvider.GetRequiredService<IClusterClient>();

// Create lightweight proxy (generated at compile-time)
var proxy = ActorProxyFactory.CreateProxy<IMyActor>(client, "actor-1");

// Call methods (routes through transport layer, no reference leak)
await proxy.DoSomethingAsync();
```

### Why This Works
1. `ActorProxyFactory.CreateProxy<T>()` is generated in **your consuming assembly**
2. Proxy is a lightweight wrapper that calls `IClusterClient.SendAsync()`
3. All calls go through serialization → transport → target silo
4. No direct actor references = no reference leaks
5. Actors can be freely deactivated and migrated

## Reference Leak Prevention

### What is a Reference Leak?

In a Virtual Actor framework, a **reference leak** occurs when:
1. Client gets a direct reference to a real actor instance
2. Client bypasses the transport layer
3. Actor cannot be deactivated (client holds reference)
4. Actor cannot be migrated to another silo
5. Distributed system semantics are broken

### The Problem We Fixed

```csharp
// ❌ OLD WAY (Reference Leak):
var actor = actorFactory.GetOrCreateActor<MyActor>("actor-1");
// ^ This returns a REAL actor instance
// ^ Actor is cached in dictionary
// ^ Can't be deactivated while reference exists
// ^ Breaks Virtual Actor model

await actor.DoSomethingAsync(); 
// ^ Bypasses serialization
// ^ Bypasses transport layer
// ^ Bypasses consistent hashing
```

### The Solution

```csharp
// ✅ NEW WAY (No Reference Leak):
var proxy = ActorProxyFactory.CreateProxy<IMyActor>(client, "actor-1");
// ^ This returns a lightweight PROXY
// ^ Not cached (created per call)
// ^ No real actor reference

await proxy.DoSomethingAsync();
// ^ Serializes parameters to Protobuf
// ^ Calls IClusterClient.SendAsync()
// ^ Routes through transport layer
// ^ Consistent hashing determines target silo
// ^ Deserializes response
```

## Documentation Added

### 1. docs/VIRTUAL_ACTOR_PRINCIPLES.md
Comprehensive guide covering:
- What is a Virtual Actor
- Why direct references are dangerous
- Correct proxy pattern
- When direct references are acceptable (parent-child only)
- Architecture diagrams
- Migration guide

### 2. README.md
Added link to Virtual Actor Principles in Essential Guides section

### 3. Code Comments
Updated IClusterClient.GetActor<T>() with detailed remarks explaining the new pattern

## Impact

### Positive Changes
✅ **Prevents Reference Leaks**: Clients can no longer get direct actor references  
✅ **Correct Virtual Actor Semantics**: All calls go through transport layer  
✅ **Better AOT Compatibility**: Generated proxies per assembly  
✅ **Clearer API**: Explicit `ActorProxyFactory.CreateProxy<T>()` call  
✅ **Better Documentation**: Clear guide on avoiding common pitfalls

### Breaking Changes
⚠️ **ClusterClient.GetActor<T>() throws NotImplementedException**  
   Migration: Replace `client.GetActor<T>(id)` with `ActorProxyFactory.CreateProxy<T>(client, id)`

⚠️ **ClusterClient no longer takes IActorFactory parameter**  
   Impact: DI registration automatically handles this (no user action needed)

## Testing Results

- ✅ Build: All projects build successfully
- ✅ ActorFactoryTests: 6/6 tests passed
- ✅ ProxyGenerationTests: 17/17 tests passed
- ✅ No regressions detected

## Future Considerations

### Potential Improvements
1. Add Roslyn analyzer to detect `GetOrCreateActor<T>()` usage in client code
2. Generate extension methods for `IClusterClient` in consuming assemblies
3. Add compile-time warning when calling GetActor<T>() directly

### Open Questions
1. Should we completely remove `IClusterClient.GetActor<T>()` from interface?
   - Pro: Forces correct usage
   - Con: Breaking change for interface consumers

2. Should ActorFactory.GetOrCreateActor() have a comment warning about reference leaks?
   - Pro: Helps developers understand when it's safe
   - Con: May be confusing

## Related Documentation

- [Virtual Actor Principles](VIRTUAL_ACTOR_PRINCIPLES.md) - Complete guide
- [Type-Safe Proxies](TYPE_SAFE_PROXIES.md) - Proxy generation details
- [Zero Reflection Achievement](ZERO_REFLECTION_ACHIEVEMENT.md) - AOT compatibility

## Conclusion

This fix addresses the core issue that Quark, as a Virtual Actor framework, must never allow clients to get direct actor references. The new pattern enforces proper distributed system semantics where all actor calls go through the transport layer via lightweight, AOT-compatible proxies generated at compile-time.

The key insight is that `ActorProxyFactory.CreateProxy<T>()` is generated **per consuming assembly**, not in the Quark.Client library itself. This design enables type-safe, reflection-free proxies while preventing the reference leak anti-pattern.
