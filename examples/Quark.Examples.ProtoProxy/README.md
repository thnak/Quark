# Quark Protobuf Proxy Generation Example

This example demonstrates the **Protobuf Proxy Generation** feature introduced in Phase 9.1 of the Quark framework.

## What This Example Shows

- How to define an actor with methods for proxy generation
- Automatic generation of type-safe client proxy interfaces
- Protocol Buffer (.proto) file generation for documentation
- Clean, compile-time-checked remote actor invocation

## The Actor

`CalculatorActor` is a simple actor with three methods:

```csharp
[Actor(Name = "Calculator")]
public class CalculatorActor : ActorBase
{
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(10);
        return a + b;
    }

    public async Task<int> MultiplyAsync(int a, int b)
    {
        await Task.Delay(10);
        return a * b;
    }

    public async Task<string> GetStatusAsync()
    {
        return $"Calculator {ActorId} is active";
    }
}
```

## What Gets Generated

When you build this project, the `ProtoSourceGenerator` creates:

### 1. Client Proxy Interface

```csharp
public interface ICalculatorActorProxy
{
    Task<int> AddAsync(int a, int b);
    Task<int> MultiplyAsync(int a, int b);
    Task<string> GetStatusAsync();
}
```

### 2. Client Proxy Implementation

```csharp
public class CalculatorActorProxy : ICalculatorActorProxy
{
    private readonly IClusterClient _client;
    private readonly string _actorId;
    
    public CalculatorActorProxy(IClusterClient client, string actorId)
    {
        _client = client;
        _actorId = actorId;
    }
    
    public async Task<int> AddAsync(int a, int b)
    {
        var envelope = CreateEnvelope("AddAsync", new object[] { a, b });
        var response = await _client.SendAsync(envelope);
        return DeserializeResponse<int>(response);
    }
    
    // ... other methods
}
```

### 3. Protocol Buffer Definition

A `.proto` file documenting the service contract:

```proto
service CalculatorService {
  rpc Add (CalculatorAddRequest) returns (CalculatorAddResponse);
  rpc Multiply (CalculatorMultiplyRequest) returns (CalculatorMultiplyResponse);
  rpc GetStatus (CalculatorGetStatusRequest) returns (CalculatorGetStatusResponse);
}
```

## Viewing Generated Files

Generated files are located in:

```
obj/Generated/Quark.Generators/Quark.Generators.ProtoSourceGenerator/
├── CalculatorActorProxy.g.cs
└── ActorServices.proto.txt.g.cs
```

## Using the Proxy (In a Real Application)

```csharp
// Connect to cluster
IClusterClient client = services.GetRequiredService<IClusterClient>();
await client.ConnectAsync();

// Get type-safe proxy
var calculator = client.GetActorProxy<ICalculatorActorProxy>("calc-001");

// Call methods with full IntelliSense
int sum = await calculator.AddAsync(5, 3);
Console.WriteLine($"5 + 3 = {sum}");

int product = await calculator.MultiplyAsync(7, 6);
Console.WriteLine($"7 * 6 = {product}");

string status = await calculator.GetStatusAsync();
Console.WriteLine(status);
```

## Benefits

- ✅ **Type Safety**: Compile-time checking of method signatures
- ✅ **IntelliSense**: Full IDE support for actor methods
- ✅ **No Strings**: No string-based method names
- ✅ **Automatic Serialization**: JSON handling built-in
- ✅ **AOT Compatible**: Zero runtime reflection
- ✅ **Clean API**: Looks like local method calls

## Building and Running

```bash
# Build the project
dotnet build

# Run the example
dotnet run

# View generated files
ls obj/Generated/Quark.Generators/Quark.Generators.ProtoSourceGenerator/
```

## Learn More

- [Proto Proxy Guide](../../docs/PROTO_PROXY_GUIDE.md) - Comprehensive usage guide
- [ENHANCEMENTS.md](../../docs/ENHANCEMENTS.md) - Phase 9.1 details
- [Quark README](../../README.md) - Framework overview

## Related Examples

- `Quark.Examples.Basic` - Basic actor lifecycle
- `Quark.Examples.Supervision` - Supervision hierarchies
- `Quark.Examples.Streaming` - Reactive streaming

---

**Feature Status:** ✅ COMPLETED  
**Phase:** 9.1 - Enhanced Source Generators  
**Date:** 2026-01-30
