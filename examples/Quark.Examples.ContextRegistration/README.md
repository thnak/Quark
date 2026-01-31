# Context-Based Actor Registration Example

This example demonstrates how to use `QuarkActorContext` to register actor interfaces for proxy generation when those interfaces don't inherit from `IQuarkActor`.

## Problem

You're working with an external library that defines actor interfaces, but you can't modify the library to make them inherit from `IQuarkActor`. For example:

```csharp
// From an external library you can't modify
public interface ICalculatorService
{
    string ActorId { get; }
    Task<int> AddAsync(int a, int b);
    Task<int> MultiplyAsync(int x, int y);
}
```

## Solution

Use `[QuarkActorContext]` and `[QuarkActor(typeof(...))]` attributes to explicitly register interfaces for proxy generation:

```csharp
[QuarkActorContext]
[QuarkActor(typeof(ICalculatorService))]
public partial class ExternalActorContext
{
}
```

## How It Works

1. The `QuarkActorContext` attribute marks a class as a registration context
2. `QuarkActor` attributes register specific interfaces for proxy generation
3. The source generator scans for these contexts at compile-time
4. Generated proxies automatically implement both the target interface AND `IQuarkActor`
5. You can use `GetActor<ICalculatorService>()` normally

## Key Benefits

- ✅ Works with external library interfaces you can't modify
- ✅ Explicit control over what gets proxy generation
- ✅ Same pattern as `System.Text.Json.JsonSerializerContext`
- ✅ Full AOT compatibility (zero reflection)
- ✅ Compile-time code generation
- ✅ Type-safe Protobuf message contracts

## Generated Files

The source generator creates:

```
obj/Generated/Quark.Generators/Quark.Generators.ProxySourceGenerator/
├── ICalculatorServiceMessages.g.cs    # Protobuf contracts
├── ICalculatorServiceProxy.g.cs       # Client proxy implementation
└── ActorProxyFactory.g.cs             # Factory registration
```

### Generated Proxy

```csharp
internal sealed class ICalculatorServiceProxy : ICalculatorService, IQuarkActor
{
    private readonly IClusterClient _client;
    private readonly string _actorId;
    
    // ... implements all methods from ICalculatorService
    // ... implements OnActivateAsync and OnDeactivateAsync from IQuarkActor
}
```

## Running the Example

```bash
dotnet run --project examples/Quark.Examples.ContextRegistration
```

## Key Files

- **ICalculatorService.cs**: External interface (doesn't inherit from IQuarkActor)
- **ExternalActorContext.cs**: Context class that registers the interface
- **CalculatorActor.cs**: Server-side implementation
- **Program.cs**: Example documentation

## Pattern Comparison

This pattern is inspired by `System.Text.Json`'s `JsonSerializerContext`:

```csharp
// JSON Source Generation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MyModel))]
public partial class MyJsonContext : JsonSerializerContext { }

// Actor Proxy Generation (Quark)
[QuarkActorContext]
[QuarkActor(typeof(IMyActor))]
public partial class MyActorContext { }
```

Both provide:
- Explicit type registration
- Compile-time code generation
- AOT compatibility
- No reflection at runtime

## Use Cases

1. **External Libraries**: When using actor interfaces from NuGet packages you can't modify
2. **Explicit Control**: When you want explicit control over which interfaces get proxy generation
3. **Multiple Interfaces**: When you need to register multiple interfaces in one place
4. **Migration**: When migrating existing code to Quark and can't change all interfaces immediately

## See Also

- [Type-Safe Proxies Documentation](../../docs/TYPE_SAFE_PROXIES.md)
- [Source Generators Wiki](../../wiki/Source-Generators.md)
- [Zero Reflection Achievement](../../docs/ZERO_REFLECTION_ACHIEVEMENT.md)
