# QuarkActorContext: Context-Based Proxy Generation

## Overview

`QuarkActorContext` provides a flexible way to register actor interfaces for proxy generation at compile-time, similar to how `System.Text.Json` uses `JsonSerializerContext` for JSON serialization.

## The Problem

The standard approach requires actor interfaces to inherit from `IQuarkActor`:

```csharp
// Standard approach - requires IQuarkActor inheritance
public interface IUserActor : IQuarkActor
{
    Task<string> GetNameAsync();
}
```

This works well for interfaces you control, but breaks down when:

1. **External Libraries**: You're using actor interfaces from a NuGet package you can't modify
2. **Existing Code**: You have existing interfaces that you can't or don't want to change
3. **Explicit Control**: You want explicit control over which interfaces get proxy generation

## The Solution

Use `QuarkActorContext` to explicitly register interfaces:

```csharp
// External interface (can't be modified)
public interface IExternalService
{
    string ActorId { get; }
    Task<string> ProcessAsync(string data);
}

// Registration context
[QuarkActorContext]
[QuarkActor(typeof(IExternalService))]
public partial class MyActorContext
{
}
```

## How It Works

### 1. Define a Context Class

Create a partial class and mark it with `[QuarkActorContext]`:

```csharp
[QuarkActorContext]
public partial class MyActorContext
{
}
```

### 2. Register Actor Interfaces

Add `[QuarkActor(typeof(...))]` attributes for each interface:

```csharp
[QuarkActorContext]
[QuarkActor(typeof(ICalculatorService))]
[QuarkActor(typeof(ICounterService))]
[QuarkActor(typeof(INotificationService))]
public partial class MyActorContext
{
}
```

### 3. Compile-Time Generation

The `ProxySourceGenerator` automatically:

1. Scans for classes with `[QuarkActorContext]`
2. Extracts registered interface types from `[QuarkActor]` attributes
3. Generates Protobuf message contracts for each method
4. Generates type-safe client proxies
5. Registers proxies in `ActorProxyFactory`

### 4. Generated Proxy Implementation

The generated proxy implements both the target interface AND `IQuarkActor`:

```csharp
// Generated code
internal sealed class IExternalServiceProxy : IExternalService, IQuarkActor
{
    private readonly IClusterClient _client;
    private readonly string _actorId;
    
    public string ActorId => _actorId;
    
    public Task OnActivateAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    
    public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    
    public async Task<string> ProcessAsync(string data)
    {
        // Serializes parameters to Protobuf
        // Sends via IClusterClient
        // Deserializes response
    }
}
```

## Usage

Once registered, use the interface normally with `IClusterClient`:

```csharp
var client = serviceProvider.GetRequiredService<IClusterClient>();
await client.ConnectAsync();

// Works even though IExternalService doesn't inherit from IQuarkActor
var service = client.GetActor<IExternalService>("service-1");
var result = await service.ProcessAsync("hello");
```

## Comparison with Standard Approach

### Standard Approach (IQuarkActor Inheritance)

**Pros:**
- Simple and straightforward
- No extra registration needed
- Clear intent (interface inherits from IQuarkActor)

**Cons:**
- Can't use with external libraries
- Requires modifying interface definitions
- All interfaces must inherit from IQuarkActor

**Example:**
```csharp
public interface IMyActor : IQuarkActor
{
    Task DoWorkAsync();
}
// Proxy generation happens automatically
```

### Context-Based Approach

**Pros:**
- Works with external library interfaces
- Explicit control over what gets generated
- Can register interfaces you don't own
- Familiar pattern (like JsonSerializerContext)

**Cons:**
- Requires additional context class
- Extra step to register interfaces
- Less obvious which interfaces have proxies

**Example:**
```csharp
[QuarkActorContext]
[QuarkActor(typeof(IMyActor))]
public partial class MyContext { }
```

## When to Use Each Approach

### Use Standard Approach (IQuarkActor) When:
- ✅ You control the interface definitions
- ✅ You're building a new application
- ✅ You want automatic discovery
- ✅ All your interfaces can inherit from IQuarkActor

### Use Context-Based Approach When:
- ✅ Working with external library interfaces
- ✅ Can't modify existing interfaces
- ✅ Want explicit control over proxy generation
- ✅ Migrating existing code to Quark
- ✅ Need to register multiple third-party interfaces

## Pattern Inspiration: JsonSerializerContext

This pattern is inspired by `System.Text.Json.Serialization.JsonSerializerContext`:

```csharp
// JSON serialization context (System.Text.Json)
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WeatherForecast))]
[JsonSerializable(typeof(User))]
public partial class MyJsonContext : JsonSerializerContext { }

// Actor proxy generation context (Quark)
[QuarkActorContext]
[QuarkActor(typeof(IWeatherActor))]
[QuarkActor(typeof(IUserActor))]
public partial class MyActorContext { }
```

Both provide:
- ✅ Explicit type registration
- ✅ Compile-time code generation
- ✅ AOT compatibility
- ✅ Zero reflection at runtime
- ✅ Full type safety

## Requirements

### Interface Requirements

Registered interfaces must:
1. Be interface types (not classes)
2. Have methods returning `Task` or `Task<T>`
3. Have serializable parameter types (primitives, strings, POCOs supported by protobuf-net)
4. Include an `ActorId` property of type `string`

### Example Valid Interface

```csharp
public interface IValidService
{
    string ActorId { get; }                                    // ✅ Required property
    Task ProcessAsync(string data);                            // ✅ Task return
    Task<int> CalculateAsync(int x, int y);                   // ✅ Task<T> return
    Task<UserData> GetUserAsync(string userId);               // ✅ POCO return type
}
```

### Example Invalid Interface

```csharp
public interface IInvalidService
{
    // ❌ Missing ActorId property
    void DoWork();                                              // ❌ void return (not async)
    int Calculate(int x);                                       // ❌ Non-task return
    Task ProcessAsync(Action<string> callback);                // ❌ Non-serializable parameter
}
```

## Advanced Usage

### Multiple Contexts

You can create multiple contexts for organization:

```csharp
// Context for payment-related actors
[QuarkActorContext]
[QuarkActor(typeof(IPaymentProcessor))]
[QuarkActor(typeof(IRefundHandler))]
public partial class PaymentActorContext { }

// Context for notification-related actors
[QuarkActorContext]
[QuarkActor(typeof(IEmailNotifier))]
[QuarkActor(typeof(ISmsNotifier))]
public partial class NotificationActorContext { }
```

### Mixing Approaches

You can use both standard and context-based approaches in the same project:

```csharp
// Standard approach for your own interfaces
public interface IMyActor : IQuarkActor
{
    Task DoWorkAsync();
}

// Context-based for external interfaces
[QuarkActorContext]
[QuarkActor(typeof(IExternalLibraryActor))]
public partial class ExternalActorContext { }

// Both work seamlessly
var myActor = client.GetActor<IMyActor>("actor-1");
var externalActor = client.GetActor<IExternalLibraryActor>("actor-2");
```

## Generated Files

For each registered interface, the generator creates:

```
obj/Generated/Quark.Generators/Quark.Generators.ProxySourceGenerator/
├── IExternalServiceMessages.g.cs      # Protobuf message contracts
├── IExternalServiceProxy.g.cs         # Client proxy implementation
└── ActorProxyFactory.g.cs             # Factory registration
```

### Message Contracts (Example)

```csharp
[ProtoContract]
public struct ProcessAsyncRequest
{
    [ProtoMember(1)] public string Data { get; set; }
}

[ProtoContract]
public struct ProcessAsyncResponse
{
    [ProtoMember(1)] public string Result { get; set; }
}
```

## Benefits

1. **External Library Support**: Generate proxies for interfaces you don't own
2. **Explicit Control**: Choose exactly which interfaces get proxy generation
3. **AOT Compatible**: Full Native AOT support with zero reflection
4. **Familiar Pattern**: Similar to JsonSerializerContext and other source generators
5. **Compile-Time Safety**: Errors caught at compile-time, not runtime
6. **Type Safety**: Strongly-typed Protobuf messages and proxies
7. **Performance**: Zero-allocation serialization with protobuf-net
8. **Backward Compatible**: Works alongside existing IQuarkActor approach

## Limitations

1. **Interface Types Only**: Can only register interface types, not classes
2. **Partial Class Required**: Context class must be marked `partial`
3. **Attribute Limit**: Limited by C# attribute restrictions
4. **Same Assembly**: Works best when context is in the same assembly as usage
5. **Compile-Time Only**: Registration must happen at compile-time, not runtime

## Troubleshooting

### "No proxy factory registered" Error

**Problem**: Getting runtime error about missing proxy factory

**Solution**: Ensure:
1. Context class has `[QuarkActorContext]` attribute
2. Interface is registered with `[QuarkActor(typeof(...))]`
3. Generator project reference includes `OutputItemType="Analyzer"`
4. Project has been rebuilt after adding the context

**Example Fix:**
```xml
<ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                  OutputItemType="Analyzer" 
                  ReferenceOutputAssembly="false" />
```

### Interface Not Discovered

**Problem**: Context class defined but interface not generating proxy

**Solution**: Check:
1. Interface is actually an interface (not a class)
2. Type parameter is correct: `typeof(IMyInterface)`
3. Clean and rebuild: `dotnet clean && dotnet build`
4. Check generated files in `obj/Generated/`

### Method Serialization Errors

**Problem**: Compilation errors about parameter serialization

**Solution**: Ensure:
1. All parameters are protobuf-serializable types
2. Return types are `Task` or `Task<T>` where T is serializable
3. Avoid delegates, events, or other non-serializable types

## Examples

See the complete working example at:
- [examples/Quark.Examples.ContextRegistration](../examples/Quark.Examples.ContextRegistration)

## See Also

- [Type-Safe Proxies Documentation](TYPE_SAFE_PROXIES.md)
- [Source Generators Wiki](../wiki/Source-Generators.md)
- [Zero Reflection Achievement](ZERO_REFLECTION_ACHIEVEMENT.md)
- [System.Text.Json Source Generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
