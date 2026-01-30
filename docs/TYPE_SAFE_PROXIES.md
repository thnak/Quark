# Type-Safe Client Proxies

This document explains how to use Quark's type-safe client proxies for remote actor invocation.

## Overview

Quark's proxy generation feature enables type-safe, compile-time validated remote actor calls without manual envelope construction. The `ProxySourceGenerator` automatically generates:

1. **Protobuf message contracts** for method parameters and return values
2. **Client-side proxy implementations** that serialize/deserialize messages
3. **Factory registrations** for `IClusterClient.GetActor<T>()`

All code generation happens at compile-time, making it fully Native AOT compatible with zero reflection.

## Quick Start

### Step 1: Define an Actor Interface

Create an interface that inherits from `IQuarkActor`:

```csharp
using Quark.Abstractions;

public interface IUserActor : IQuarkActor
{
    Task<string> GetNameAsync();
    Task UpdateEmailAsync(string newEmail);
    Task<bool> ValidateCredentialsAsync(string username, string password);
}
```

**Rules:**
- Interface must inherit from `IQuarkActor`
- All methods must return `Task` or `Task<T>` (async methods only)
- Parameters must be serializable by protobuf-net (primitives, strings, POCOs)

### Step 2: Implement the Actor (Server-Side)

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

[Actor(Name = "User")]
public class UserActor : ActorBase, IUserActor
{
    private string _name = "Unknown";
    private string _email = "";

    public UserActor(string actorId) : base(actorId) { }

    public Task<string> GetNameAsync()
    {
        return Task.FromResult(_name);
    }

    public Task UpdateEmailAsync(string newEmail)
    {
        _email = newEmail;
        return Task.CompletedTask;
    }

    public Task<bool> ValidateCredentialsAsync(string username, string password)
    {
        // Validation logic here
        return Task.FromResult(username == _name);
    }
}
```

### Step 3: Use the Proxy (Client-Side)

```csharp
using Quark.Client;

// Get the cluster client (from DI)
var client = serviceProvider.GetRequiredService<IClusterClient>();
await client.ConnectAsync();

// Get a type-safe proxy
var user = client.GetActor<IUserActor>("user-123");

// Make strongly-typed calls
var name = await user.GetNameAsync();
await user.UpdateEmailAsync("newemail@example.com");
var isValid = await user.ValidateCredentialsAsync("john", "secret");
```

## Generated Code

For the `IUserActor` interface above, the source generator creates:

### 1. Protobuf Message Contracts

```csharp
// Generated file: IUserActorMessages.g.cs
[ProtoContract]
public struct UpdateEmailAsyncRequest
{
    [ProtoMember(1)] public string NewEmail { get; set; }
}

[ProtoContract]
public struct GetNameAsyncResponse
{
    [ProtoMember(1)] public string Result { get; set; }
}

[ProtoContract]
public struct ValidateCredentialsAsyncRequest
{
    [ProtoMember(1)] public string Username { get; set; }
    [ProtoMember(2)] public string Password { get; set; }
}

[ProtoContract]
public struct ValidateCredentialsAsyncResponse
{
    [ProtoMember(1)] public bool Result { get; set; }
}
```

### 2. Client-Side Proxy

```csharp
// Generated file: IUserActorProxy.g.cs
internal sealed class IUserActorProxy : IUserActor
{
    private readonly IClusterClient _client;
    private readonly string _actorId;

    public IUserActorProxy(IClusterClient client, string actorId)
    {
        _client = client;
        _actorId = actorId;
    }

    public string ActorId => _actorId;

    public async Task<string> GetNameAsync()
    {
        var envelope = new QuarkEnvelope(
            messageId: Guid.NewGuid().ToString(),
            actorId: _actorId,
            actorType: "UserActor",
            methodName: "GetNameAsync",
            payload: Array.Empty<byte>());

        var response = await _client.SendAsync(envelope, default);
        
        if (response.IsError)
            throw new InvalidOperationException($"Actor call failed: {response.ErrorMessage}");

        using var ms = new MemoryStream(response.ResponsePayload);
        var result = Serializer.Deserialize<GetNameAsyncResponse>(ms);
        return result.Result;
    }

    // ... other methods
}
```

### 3. Factory Registration

```csharp
// Generated file: ActorProxyFactory.g.cs
internal static partial class ActorProxyFactory
{
    public static TActorInterface CreateProxy<TActorInterface>(
        IClusterClient client, string actorId)
        where TActorInterface : class, IQuarkActor
    {
        if (typeof(TActorInterface) == typeof(IUserActor))
            return (TActorInterface)(object)new IUserActorProxy(client, actorId);

        throw new InvalidOperationException(
            $"No proxy factory registered for {typeof(TActorInterface).FullName}");
    }
}
```

## Advanced Features

### Complex Types

Protobuf-net supports complex types as parameters:

```csharp
[ProtoContract]
public class UserProfile
{
    [ProtoMember(1)] public string Name { get; set; }
    [ProtoMember(2)] public int Age { get; set; }
    [ProtoMember(3)] public List<string> Tags { get; set; }
}

public interface IProfileActor : IQuarkActor
{
    Task UpdateProfileAsync(UserProfile profile);
    Task<UserProfile> GetProfileAsync();
}
```

**Note:** Custom types must have `[ProtoContract]` and `[ProtoMember]` attributes.

### Error Handling

The generated proxy automatically handles errors:

```csharp
try
{
    await userActor.UpdateEmailAsync("invalid@email");
}
catch (InvalidOperationException ex)
{
    // Thrown when server returns IsError = true
    Console.WriteLine($"Actor error: {ex.Message}");
}
```

### Multiple Interfaces

An actor can implement multiple interfaces:

```csharp
public interface IReadableActor : IQuarkActor
{
    Task<string> GetDataAsync();
}

public interface IWritableActor : IQuarkActor
{
    Task SetDataAsync(string data);
}

[Actor]
public class DataActor : ActorBase, IReadableActor, IWritableActor
{
    // Implement both interfaces
}

// Client side - use either interface
var reader = client.GetActor<IReadableActor>("data-1");
var writer = client.GetActor<IWritableActor>("data-1");
```

## Performance

Protobuf serialization provides excellent performance characteristics:

- **Binary format**: Compact message size (typically 2-10x smaller than JSON)
- **Fast serialization**: protobuf-net is one of the fastest serializers for .NET
- **Zero allocation**: Source-generated code minimizes allocations
- **AOT-compatible**: Full Native AOT support with no reflection overhead

### Benchmarks

Typical overhead for a proxy call (excluding network latency):

- Serialization: ~50-200 ns (depending on parameter complexity)
- Deserialization: ~50-200 ns
- Envelope construction: ~20 ns
- Total client-side overhead: **~150-500 ns**

## Troubleshooting

### "No proxy factory registered" Error

**Problem:** `InvalidOperationException: No proxy factory registered for actor interface type 'MyNamespace.IMyActor'`

**Solution:** Ensure:
1. The interface inherits from `IQuarkActor`
2. The project references `Quark.Generators` as an analyzer:
   ```xml
   <ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                     OutputItemType="Analyzer" 
                     ReferenceOutputAssembly="false" />
   ```

### Generated Files Not Visible

**Problem:** Can't see generated proxy files in IDE

**Solution:** Generated files are in `obj/Debug/net10.0/generated/` by default. To make them visible:

```bash
dotnet build /p:EmitCompilerGeneratedFiles=true
```

This creates files in a `Generated/` folder in your project.

### Protobuf Serialization Errors

**Problem:** Custom types fail to serialize

**Solution:** Ensure all custom types have protobuf-net attributes:

```csharp
[ProtoContract]
public class MyCustomType
{
    [ProtoMember(1)] public string Property1 { get; set; }
    [ProtoMember(2)] public int Property2 { get; set; }
}
```

## Best Practices

1. **Interface design**: Keep actor interfaces focused and cohesive
2. **Parameter types**: Use simple types when possible (primitives, strings, POCOs)
3. **Method naming**: Use descriptive async method names ending with `Async`
4. **Error handling**: Always wrap actor calls in try-catch blocks
5. **Actor IDs**: Use meaningful, deterministic actor IDs for consistent routing

## Limitations

1. **Method restrictions**: Only async methods (returning `Task` or `Task<T>`) are supported
2. **Parameter types**: Parameters must be protobuf-serializable
3. **No ref/out parameters**: Reference and output parameters are not supported
4. **No overloading**: Method overloading is not recommended (can cause ambiguity)

## See Also

- [SOURCE_GENERATOR_SETUP.md](SOURCE_GENERATOR_SETUP.md) - Source generator setup guide
- [ZERO_REFLECTION_ACHIEVEMENT.md](ZERO_REFLECTION_ACHIEVEMENT.md) - How Quark achieves zero reflection
- [protobuf-net documentation](https://github.com/protobuf-net/protobuf-net) - Protobuf serialization library
