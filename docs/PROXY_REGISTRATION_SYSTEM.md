# Actor Proxy Registration System

## Overview

Quark uses a registration-based system for actor proxies to maintain AOT compatibility while providing type-safe, Orleans-compatible actor calls. This document explains how proxy registration works and how to use it.

## Key Components

### 1. ActorProxyFactory (Public Static Class)

The `ActorProxyFactory` class provides the core proxy creation and registration API:

```csharp
public static class ActorProxyFactory
{
    // Create a proxy for a registered actor interface
    public static TActorProxy CreateProxy<TActorProxy>(IClusterClient client, string actorId);
    
    // Register a proxy factory for an actor interface
    public static void RegisterProxyFactory<TActorProxy>(
        Func<IClusterClient, string, IQuarkActor> factory);
}
```

### 2. Generated Registration Class

For each assembly with actor interfaces, the `ProxySourceGenerator` generates a registration class:

```csharp
namespace Quark.Generated
{
    public static class {AssemblyName}ActorProxyFactoryRegistration
    {
        public static void RegisterAll()
        {
            // Registers all actor proxy factories for this assembly
            ActorProxyFactory.RegisterProxyFactory<IMyActor>(
                (client, actorId) => new MyNamespace.Generated.MyActorProxy(client, actorId));
            // ... more registrations
        }
    }
}
```

### 3. Proxy Class Naming

Proxies are named **without the 'I' prefix**:

- Interface: `ICounterActor`
- Proxy class: `CounterActorProxy` (NOT `ICounterActorProxy`)
- Generated in: `{Namespace}.Generated` namespace

## Usage Patterns

### Pattern 1: Auto-Generated Registration (Recommended)

Call the generated `RegisterAll()` method once at application startup:

```csharp
using Quark.Generated;

// In your application startup (Program.cs, Startup.cs, etc.)
MyAssemblyActorProxyFactoryRegistration.RegisterAll();

// Then use proxies anywhere in your code
var client = serviceProvider.GetRequiredService<IClusterClient>();
var proxy = ActorProxyFactory.CreateProxy<IOrderActor>(client, "order-123");
await proxy.ProcessOrderAsync();
```

**Benefits:**
- One-line registration for all proxies
- Idempotent (safe to call multiple times)
- AOT-compatible and trimmable
- Type-safe

### Pattern 2: Manual Registration

Register individual proxy factories manually:

```csharp
using MyNamespace.Generated;

// Register each proxy factory manually
ActorProxyFactory.RegisterProxyFactory<IOrderActor>(
    (client, actorId) => new OrderActorProxy(client, actorId));
    
ActorProxyFactory.RegisterProxyFactory<ICustomerActor>(
    (client, actorId) => new CustomerActorProxy(client, actorId));

// Then use proxies
var proxy = ActorProxyFactory.CreateProxy<IOrderActor>(client, "order-123");
```

**Benefits:**
- Fine-grained control over what's registered
- Can register selectively
- Useful for testing scenarios

### Pattern 3: Module Initializer (For Libraries)

Use a module initializer to auto-register proxies when your assembly loads:

```csharp
using System.Runtime.CompilerServices;
using Quark.Generated;

namespace MyLibrary;

internal static class ProxyInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        MyLibraryActorProxyFactoryRegistration.RegisterAll();
    }
}
```

**Benefits:**
- Automatic registration when assembly loads
- No manual initialization required by consumers
- Perfect for shared libraries

## Orleans Compatibility

The system maintains compatibility with Microsoft Orleans patterns:

```csharp
// Orleans-style GetActor (works via IClusterClient)
var actor = client.GetActor<IOrderActor>("order-123");
await actor.ProcessOrderAsync();

// Under the hood, calls ActorProxyFactory.CreateProxy
```

`IClusterClient.GetActor<TActorInterface>()` internally calls `ActorProxyFactory.CreateProxy<TActorInterface>()`, maintaining the familiar Orleans API while using the registration system.

## Registration Class Naming

The generated registration class name follows this pattern:

```
{AssemblyName}ActorProxyFactoryRegistration
```

Examples:
- Assembly: `Quark.Tests` → Class: `QuarkTestsActorProxyFactoryRegistration`
- Assembly: `My.Orders.Service` → Class: `MyOrdersServiceActorProxyFactoryRegistration`
- Assembly: `Awesome.Pizza` → Class: `AwesomePizzaActorProxyFactoryRegistration`

Note: Dots in assembly names are removed to create valid C# identifiers.

## Example: Complete Setup

### 1. Define Actor Interface

```csharp
using Quark.Abstractions;

namespace MyApp.Actors;

public interface IOrderActor : IQuarkActor
{
    Task<Order> GetOrderAsync(string orderId);
    Task UpdateStatusAsync(OrderStatus status);
}
```

### 2. Implement Actor

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

namespace MyApp.Actors;

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

### 3. Register Proxies at Startup

```csharp
using Quark.Client;
using Quark.Generated;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Configure Quark Client
builder.Services.UseQuarkClient(
    configure: options => { /* config */ },
    clientBuilderConfigure: clientBuilder => {
        clientBuilder.WithRedisClustering("localhost");
        clientBuilder.WithGrpcTransport();
    });

// Register all actor proxy factories
MyAppActorProxyFactoryRegistration.RegisterAll();

var app = builder.Build();
app.Run();
```

### 4. Use Proxies

```csharp
public class OrderController : ControllerBase
{
    private readonly IClusterClient _client;
    
    public OrderController(IClusterClient client)
    {
        _client = client;
    }
    
    [HttpGet("orders/{id}")]
    public async Task<Order> GetOrder(string id)
    {
        // Option 1: Use ActorProxyFactory directly
        var proxy = ActorProxyFactory.CreateProxy<IOrderActor>(_client, $"order-{id}");
        return await proxy.GetOrderAsync(id);
        
        // Option 2: Use Orleans-style GetActor
        var actor = _client.GetActor<IOrderActor>($"order-{id}");
        return await actor.GetOrderAsync(id);
    }
}
```

## Testing

For test projects, use a module initializer to auto-register:

```csharp
using System.Runtime.CompilerServices;
using Quark.Generated;

namespace MyApp.Tests;

internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        MyAppTestsActorProxyFactoryRegistration.RegisterAll();
    }
}
```

This ensures all proxies are registered before any tests run.

## Error Handling

### Common Errors

**1. Proxy Not Registered**
```
InvalidOperationException: No proxy factory registered for actor interface type 'MyApp.IOrderActor'.
Ensure you have called the RegisterAll() method from your assembly's generated registration class.
```

**Solution:** Call `{AssemblyName}ActorProxyFactoryRegistration.RegisterAll()` before using proxies.

**2. Proxy Class Not Found**
```
CS0246: The type or namespace name 'IOrderActorProxy' could not be found
```

**Solution:** Proxy classes are named **without 'I' prefix**. Use `OrderActorProxy` instead of `IOrderActorProxy`.

**3. Assembly Name Contains Dots**
```
// Assembly: My.Orders.Service
// Generated class: MyOrdersServiceActorProxyFactoryRegistration (dots removed)
```

## AOT Compatibility

The registration system is fully AOT-compatible:

- ✅ No reflection at runtime
- ✅ All proxy factories registered at compile-time
- ✅ Generic type parameters resolved statically
- ✅ Trimmable (unused proxies can be trimmed)
- ✅ Works with Native AOT compilation

## Migration from Old System

### Before (Internal Partial Class)

```csharp
// Old: Generated internal partial class ActorProxyFactory
// Usage: ActorProxyFactory.CreateProxy<IOrderActor>(client, "order-1")
// No registration needed - magic partial class
```

### After (Registration System)

```csharp
// New: Public static class with registration
// 1. Register at startup
MyAppActorProxyFactoryRegistration.RegisterAll();

// 2. Use same API
var proxy = ActorProxyFactory.CreateProxy<IOrderActor>(client, "order-1");
```

**Migration Steps:**
1. Update proxy class references to remove 'I' prefix
2. Add registration call at application startup
3. Test that all proxies are registered before use

## Best Practices

1. **Register Once**: Call `RegisterAll()` once at application startup
2. **Module Initializer for Libraries**: Use `[ModuleInitializer]` in shared libraries
3. **Check Generated Files**: Verify generated registration class exists in `Quark.Generated` namespace
4. **Orleans Compatibility**: Use `IClusterClient.GetActor<T>()` for Orleans compatibility
5. **Error Messages**: Read error messages carefully - they guide you to the solution

## Summary

- ✅ Use `ActorProxyFactory.CreateProxy<T>()` to create proxies
- ✅ Call `{AssemblyName}ActorProxyFactoryRegistration.RegisterAll()` at startup
- ✅ Proxy classes named without 'I' prefix (e.g., `OrderActorProxy`)
- ✅ Orleans-compatible via `IClusterClient.GetActor<T>()`
- ✅ Fully AOT-compatible and trimmable
