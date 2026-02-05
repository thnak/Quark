# Binary Converter System

## Overview

The Binary Converter System replaces JSON serialization with a custom binary serialization approach that gives users full control over how actor method parameters and return values are serialized.

## Key Components

### 1. IQuarkBinaryConverter

Base interface for all binary converters:

```csharp
public interface IQuarkBinaryConverter
{
    void Write(BinaryWriter writer, object? value);
    object? Read(BinaryReader reader);
}

public interface IQuarkBinaryConverter<T> : IQuarkBinaryConverter
{
    void Write(BinaryWriter writer, T value);
    new T Read(BinaryReader reader);
}
```

### 2. QuarkBinaryConverter<T>

Abstract base class that users extend:

```csharp
public abstract class QuarkBinaryConverter<T> : IQuarkBinaryConverter<T>
{
    public abstract void Write(BinaryWriter writer, T value);
    public abstract T Read(BinaryReader reader);
}
```

### 3. BinaryConverterAttribute

Method-level attribute for specifying converters:

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class BinaryConverterAttribute : Attribute
{
    public BinaryConverterAttribute(Type converterType);
    
    public int Order { get; set; }           // For ordering multiple converters
    public string? ParameterName { get; set; } // Which parameter this applies to
}
```

## Usage Examples

### Basic Usage - Single Parameter

```csharp
public interface IUserActor : IQuarkActor
{
    [BinaryConverter(typeof(StringConverter), ParameterName = "userId")]
    Task DeleteUserAsync(string userId);
}
```

### Multiple Parameters

```csharp
public interface IOrderActor : IQuarkActor
{
    [BinaryConverter(typeof(StringConverter), ParameterName = "orderId", Order = 0)]
    [BinaryConverter(typeof(Int32Converter), ParameterName = "quantity", Order = 1)]
    [BinaryConverter(typeof(DoubleConverter), ParameterName = "price", Order = 2)]
    Task CreateOrderAsync(string orderId, int quantity, double price);
}
```

### Return Value Converter

```csharp
public interface ICalculatorActor : IQuarkActor
{
    [BinaryConverter(typeof(Int32Converter), ParameterName = "a")]
    [BinaryConverter(typeof(Int32Converter), ParameterName = "b")]
    [BinaryConverter(typeof(Int32Converter))] // No ParameterName = return value
    Task<int> AddAsync(int a, int b);
}
```

### Custom Converter

```csharp
// Define custom type
public class CustomerInfo
{
    public string Name { get; set; }
    public DateTime JoinDate { get; set; }
    public decimal Balance { get; set; }
}

// Implement custom converter
public class CustomerInfoConverter : QuarkBinaryConverter<CustomerInfo>
{
    public override void Write(BinaryWriter writer, CustomerInfo value)
    {
        writer.Write(value.Name ?? string.Empty);
        writer.Write(value.JoinDate.ToBinary());
        writer.Write(value.Balance);
    }
    
    public override CustomerInfo Read(BinaryReader reader)
    {
        return new CustomerInfo
        {
            Name = reader.ReadString(),
            JoinDate = DateTime.FromBinary(reader.ReadInt64()),
            Balance = reader.ReadDecimal()
        };
    }
}

// Use custom converter
public interface ICustomerActor : IQuarkActor
{
    [BinaryConverter(typeof(CustomerInfoConverter), ParameterName = "customer")]
    Task RegisterAsync(CustomerInfo customer);
}
```

## Built-in Converters

The framework provides converters for common types:

| Type | Converter Class |
|------|----------------|
| `string` | `StringConverter` |
| `int` | `Int32Converter` |
| `long` | `Int64Converter` |
| `bool` | `BooleanConverter` |
| `double` | `DoubleConverter` |
| `Guid` | `GuidConverter` |
| `DateTime` | `DateTimeConverter` |
| `byte[]` | `ByteArrayConverter` |

## Generated Code

### Proxy Generation (Client Side)

Given this interface:

```csharp
public interface IMyActor : IQuarkActor
{
    [BinaryConverter(typeof(StringConverter), ParameterName = "name")]
    [BinaryConverter(typeof(Int32Converter), ParameterName = "age")]
    Task UpdateAsync(string name, int age);
}
```

The generator creates:

```csharp
public sealed class MyActorProxy : IMyActor
{
    private readonly IClusterClient _client;
    private readonly string _actorId;
    
    public Task UpdateAsync(string name, int age)
    {
        byte[] payload;
        using (var ms = new MemoryStream())
        {
            using (var writer = new BinaryWriter(ms))
            {
                new StringConverter().Write(writer, name);
                new Int32Converter().Write(writer, age);
            }
            payload = ms.ToArray();
        }
        
        var envelope = new QuarkEnvelope(
            messageId: Guid.NewGuid().ToString(),
            actorId: _actorId,
            actorType: "IMyActor",
            methodName: "UpdateAsync",
            payload: payload);
            
        return _client.SendAsync(envelope, default);
    }
}
```

### Dispatcher Generation (Server Side)

```csharp
internal sealed class MyActorDispatcher : IActorMethodDispatcher
{
    public async Task<byte[]> InvokeAsync(
        IActor actor,
        string methodName,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        switch (methodName)
        {
            case "UpdateAsync":
                {
                    using (var ms = new MemoryStream(payload))
                    {
                        using (var reader = new BinaryReader(ms))
                        {
                            var name = new StringConverter().Read(reader);
                            var age = new Int32Converter().Read(reader);
                            
                            var typedActor = (MyActor)actor;
                            await typedActor.UpdateAsync(name, age);
                            return Array.Empty<byte>();
                        }
                    }
                }
        }
    }
}
```

## Migration from JSON

### Before (JSON Serialization)

```csharp
public interface IChefActor : IQuarkActor
{
    Task InitializeAsync(string name);
}

// Generated contract class (no longer needed):
public sealed class IChefActor_InitializeAsyncRequest
{
    public string Name { get; set; }
}

// Generated serialization:
var jsonRequest = new IChefActor_InitializeAsyncRequest { Name = name };
var payload = JsonSerializer.SerializeToUtf8Bytes(jsonRequest);
```

### After (Binary Converters)

```csharp
public interface IChefActor : IQuarkActor
{
    [BinaryConverter(typeof(StringConverter), ParameterName = "name")]
    Task InitializeAsync(string name);
}

// No contract class needed!

// Generated serialization:
using (var writer = new BinaryWriter(ms))
{
    new StringConverter().Write(writer, name);
}
```

## Benefits

1. **No Intermediate Classes**: Eliminates DTOs like `IChefActor_InitializeAsyncRequest`
2. **Direct Control**: Users control exactly how data is serialized
3. **Performance**: Binary serialization is typically faster than JSON
4. **Type Safety**: Compiler ensures converters match parameter types
5. **Extensibility**: Easy to add custom converters for complex types
6. **AOT Friendly**: All types known at compile time, no reflection

## Best Practices

### 1. Ordering

When using multiple converters, always specify `Order`:

```csharp
[BinaryConverter(typeof(StringConverter), ParameterName = "id", Order = 0)]
[BinaryConverter(typeof(Int32Converter), ParameterName = "count", Order = 1)]
Task ProcessAsync(string id, int count);
```

### 2. Version Compatibility

When evolving converters, maintain backward compatibility:

```csharp
public class CustomerConverterV2 : QuarkBinaryConverter<Customer>
{
    public override void Write(BinaryWriter writer, Customer value)
    {
        writer.Write(2); // Version marker
        writer.Write(value.Name);
        writer.Write(value.Email);  // New field in V2
    }
    
    public override Customer Read(BinaryReader reader)
    {
        var version = reader.ReadInt32();
        var name = reader.ReadString();
        var email = version >= 2 ? reader.ReadString() : string.Empty;
        
        return new Customer { Name = name, Email = email };
    }
}
```

### 3. Null Handling

Handle nulls explicitly in custom converters:

```csharp
public override void Write(BinaryWriter writer, MyClass? value)
{
    writer.Write(value != null); // Write null flag
    if (value != null)
    {
        writer.Write(value.Property1);
        writer.Write(value.Property2);
    }
}

public override MyClass? Read(BinaryReader reader)
{
    var hasValue = reader.ReadBoolean();
    if (!hasValue) return null;
    
    return new MyClass
    {
        Property1 = reader.ReadString(),
        Property2 = reader.ReadInt32()
    };
}
```

## Troubleshooting

### Missing Converter Error

```
Error: No default converter for type MyCustomType
```

**Solution**: Add a `[BinaryConverter]` attribute:

```csharp
[BinaryConverter(typeof(MyCustomTypeConverter), ParameterName = "param")]
Task MethodAsync(MyCustomType param);
```

### Order Not Specified

When using multiple converters without `Order`, behavior is undefined.

**Solution**: Always specify `Order`:

```csharp
[BinaryConverter(typeof(Converter1), ParameterName = "p1", Order = 0)]
[BinaryConverter(typeof(Converter2), ParameterName = "p2", Order = 1)]
```

### Type Mismatch

```
Error: Cannot convert System.String to System.Int32
```

**Solution**: Ensure converter type matches parameter type:

```csharp
// Wrong:
[BinaryConverter(typeof(StringConverter), ParameterName = "count")]
Task ProcessAsync(int count); // int needs Int32Converter!

// Correct:
[BinaryConverter(typeof(Int32Converter), ParameterName = "count")]
Task ProcessAsync(int count);
```

## Future Enhancements

1. **Analyzer Support**: Validate converter ordering and type matching at compile time
2. **Auto-Discovery**: Automatically find and apply converters without attributes for simple types
3. **Compression**: Built-in support for compressed binary streams
4. **Encryption**: Optional encryption layer for sensitive data
5. **Code Fixes**: IDE integration to auto-add converter attributes

## See Also

- [Quark Actor Model](ACTOR_MODEL.md)
- [AOT Compatibility](AOT_JSON_SERIALIZATION.md)
- [Custom Serialization Guide](CUSTOM_SERIALIZATION.md)
