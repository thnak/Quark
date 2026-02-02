# ProtoSerializer Context Registration

## Overview

Quark provides a type registration system similar to `System.Text.Json`'s `JsonSerializerContext` for ProtoBuf serialization. This allows you to:

1. **Explicitly register types** for serialization outside your core code
2. **Define custom converters** for complex serialization scenarios
3. **Support AOT compilation** by avoiding runtime reflection

## Quick Start

### 1. Add ProtoBuf Attributes to Your Types

All types used in actor interfaces must have ProtoBuf serialization attributes:

```csharp
using ProtoBuf;

[ProtoContract]
public record OrderState(
    [property: ProtoMember(1)] string OrderId,
    [property: ProtoMember(2)] string CustomerId,
    [property: ProtoMember(3)] OrderStatus Status
);
```

### 2. Create a ProtoSerializer Context

Define a context class that registers all your serializable types:

```csharp
using Quark.Abstractions;

[ProtoSerializerContext(Name = "MyApp")]
[ProtoInclude(typeof(OrderState))]
[ProtoInclude(typeof(CreateOrderRequest))]
[ProtoInclude(typeof(CreateOrderResponse))]
public sealed partial class MyAppSerializerContext : IProtoSerializerContext
{
    private static readonly Type[] _registeredTypes = new[]
    {
        typeof(OrderState),
        typeof(CreateOrderRequest),
        typeof(CreateOrderResponse)
    };

    private static readonly Dictionary<Type, Type> _customConverters = new();

    public IReadOnlyCollection<Type> RegisteredTypes => _registeredTypes;
    public IReadOnlyDictionary<Type, Type> CustomConverters => _customConverters;
    public string ContextName => "MyApp";

    public static MyAppSerializerContext Instance { get; } = new();
    
    private MyAppSerializerContext() { }
}
```

### 3. Register Your Context at Startup

```csharp
using Quark.Core.Serialization;

// In your Program.cs or startup code
ProtoSerializerContextRegistry.Instance.AutoRegisterContexts(
    typeof(MyAppSerializerContext).Assembly
);
```

## Analyzer Support

### QUARK014: Missing [ProtoContract]

The analyzer detects types used in actor interfaces that lack the `[ProtoContract]` attribute:

```csharp
public interface IOrderActor : IQuarkActor
{
    // ❌ Error QUARK014: OrderState is missing [ProtoContract]
    Task<OrderState> GetOrderAsync();
}
```

**Fix**: Add `[ProtoContract]` to the type:

```csharp
[ProtoContract]
public record OrderState(/* ... */);
```

### QUARK015: Missing [ProtoMember]

The analyzer detects properties in types with `[ProtoContract]` that lack `[ProtoMember]` attributes:

```csharp
[ProtoContract]
public record OrderState(
    string OrderId,  // ❌ Error QUARK015: Missing [ProtoMember]
    string CustomerId  // ❌ Error QUARK015: Missing [ProtoMember]
);
```

**Fix**: Add `[ProtoMember(n)]` to all properties:

```csharp
[ProtoContract]
public record OrderState(
    [property: ProtoMember(1)] string OrderId,
    [property: ProtoMember(2)] string CustomerId
);
```

### Code Fix Provider

The analyzer includes a code fix provider that can automatically add missing attributes:

1. Place your cursor on the error
2. Press `Ctrl+.` (or `Cmd+.` on Mac)
3. Select "Add [ProtoContract] and [ProtoMember] attributes"

## Custom Converters

For complex serialization scenarios, you can define custom converters:

### 1. Implement IProtoConverter<T>

```csharp
using Quark.Abstractions;

public class CustomDateTimeConverter : IProtoConverter<DateTime>
{
    public void Serialize(Stream stream, DateTime value)
    {
        // Custom serialization logic
        var ticks = value.Ticks;
        ProtoBuf.Serializer.Serialize(stream, ticks);
    }

    public DateTime Deserialize(Stream stream)
    {
        // Custom deserialization logic
        var ticks = ProtoBuf.Serializer.Deserialize<long>(stream);
        return new DateTime(ticks);
    }
}
```

### 2. Register the Converter in Your Context

```csharp
[ProtoSerializerContext]
[ProtoInclude(typeof(MyCustomType), ConverterType = typeof(MyCustomConverter))]
public sealed partial class MyAppSerializerContext : IProtoSerializerContext
{
    private static readonly Dictionary<Type, Type> _customConverters = new()
    {
        { typeof(MyCustomType), typeof(MyCustomConverter) }
    };

    // ... rest of implementation
}
```

## Best Practices

1. **Use Sequential Numbering**: Assign ProtoMember numbers sequentially starting from 1
2. **Never Reuse Numbers**: Once a property is assigned a number, never reuse it (even if the property is removed)
3. **Register All Types**: Include all types used in actor interface methods
4. **Test Serialization**: Verify serialization works before deploying to production
5. **Document Changes**: Keep a changelog of ProtoMember number assignments

## Example: AwesomePizza

See `productExample/src/Quark.AwesomePizza.Shared/Constants/PizzaProtoSerializerContext.cs` for a complete example of a serializer context with multiple types registered.

## Troubleshooting

### Runtime Error: No serializer defined

**Problem**: Type is being used in actor calls but isn't registered.

**Solution**: Add the type to your ProtoSerializer context or add `[ProtoContract]` attribute.

### Build Error: QUARK014 or QUARK015

**Problem**: Analyzer detected missing attributes.

**Solution**: Use the code fix provider or manually add the attributes.

### AOT Compatibility Issues

**Problem**: Warnings about reflection usage.

**Solution**: Ensure all types are registered in a ProtoSerializer context. The registry uses reflection only at startup, not during serialization.

## API Reference

### Attributes

- `[ProtoSerializerContext]`: Marks a class as a serializer context
- `[ProtoInclude(typeof(T))]`: Registers a type in the context
- `[ProtoContract]`: Marks a type as serializable
- `[ProtoMember(n)]`: Marks a property for serialization

### Interfaces

- `IProtoSerializerContext`: Interface for serializer contexts
- `IProtoConverter<T>`: Interface for custom converters

### Classes

- `ProtoSerializerContextRegistry`: Singleton registry for managing contexts

## Related Documentation

- [ProtoBuf-Net Documentation](https://github.com/protobuf-net/protobuf-net)
- [Quark Actor Model](../../docs/ACTOR_MODEL.md)
- [Quark Source Generators](../../docs/SOURCE_GENERATOR_SETUP.md)
