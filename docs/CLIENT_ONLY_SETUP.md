# Quark Client-Only Configuration Guide

## Overview

Quark now provides separate dependency injection packages for client-only and silo configurations:

- **`Quark.Client.DependencyInjection`**: For client-only applications that need to call actors but don't host them
- **`Quark.Extensions.DependencyInjection`**: For silo applications that host actors

## Problem Solved

Previously, the `Quark.Extensions.DependencyInjection` package mixed client and silo concerns. This caused issues:

- `RedisClusterMembership` required `CurrentSiloId` from `QuarkSiloOptions`
- Client-only apps would fail at runtime with "No service for type QuarkSiloOptions"
- Clients don't need silo registration/heartbeat features

## Client-Only Setup

### Installation

```bash
dotnet add package Quark.Client.DependencyInjection
```

### Basic Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Add Quark cluster client
builder.Services.UseQuarkClient(
    configure: options => 
    {
        options.ClientId = "my-client-app";
        options.MaxRetries = 3;
        options.RetryDelay = TimeSpan.FromSeconds(1);
    },
    clientBuilderConfigure: clientBuilder =>
    {
        // Add Redis clustering (client-only, read-only membership)
        clientBuilder.WithRedisClustering(
            connectionString: "localhost:6379");
        
        // Add gRPC transport
        clientBuilder.WithGrpcTransport();
        
        // Optional: Add health check
        clientBuilder.AddHealthCheck();
    });

var app = builder.Build();
await app.RunAsync();
```

### Using the Client

```csharp
public class MyService
{
    private readonly IClusterClient _client;

    public MyService(IClusterClient client)
    {
        _client = client;
    }

    public async Task CallActorAsync()
    {
        // Get a typed proxy for an actor
        var counter = _client.GetActor<ICounterActor>("counter-1");
        
        // Call methods on the actor
        await counter.IncrementAsync();
        var value = await counter.GetValueAsync();
    }
}
```

## Architecture

### RedisClientClusterMembership

The new `RedisClientClusterMembership` class provides read-only cluster membership for clients:

- **No registration**: Clients don't register themselves as silos
- **No heartbeats**: Clients don't send heartbeat updates  
- **Discovery only**: Clients only discover and route to existing silos
- **Empty CurrentSiloId**: Returns empty string since clients aren't silos

```csharp
public sealed class RedisClientClusterMembership : IQuarkClusterMembership
{
    public string CurrentSiloId => string.Empty; // Clients don't have a silo ID

    // These throw NotSupportedException:
    public Task RegisterSiloAsync(...)
    public Task UnregisterSiloAsync(...)
    public Task UpdateHeartbeatAsync(...)

    // These work normally for discovery:
    public Task<IReadOnlyCollection<SiloInfo>> GetActiveSilosAsync(...)
    public Task<SiloInfo?> GetSiloAsync(...)
    public string? GetActorSilo(...)
}
```

### Extension Methods

Client-specific extensions in `Quark.Client.DependencyInjection`:

- `UseQuarkClient()` / `AddQuarkClient()` - Register cluster client
- `WithRedisClustering()` - Add Redis-based cluster membership (client-only)
- `WithGrpcTransport()` - Add gRPC transport for remote calls
- `AddHealthCheck()` - Add client health check

## Co-Hosted Scenarios

For applications that both host actors (silo) AND call actors (client), you need both packages:

```bash
dotnet add package Quark.Extensions.DependencyInjection
dotnet add package Quark.Client.DependencyInjection
```

```csharp
using Quark.Extensions.DependencyInjection;
using Quark.Client.DependencyInjection;

// Add silo (hosts actors)
builder.Services.UseQuark(
    configure: siloOptions => siloOptions.SiloId = "silo-1",
    siloConfigure: siloBuilder =>
    {
        siloBuilder.WithRedisClustering(connectionString: "localhost:6379");
        siloBuilder.WithGrpcTransport();
    });

// Add client (calls actors)
builder.Services.UseQuarkClient(
    configure: clientOptions => clientOptions.ClientId = "client-1",
    clientBuilderConfigure: clientBuilder =>
    {
        clientBuilder.WithRedisClustering(connectionString: "localhost:6379");
        clientBuilder.WithGrpcTransport();
    });
```

## Migration Guide

### From Old (Mixed) Configuration

**Before:**
```csharp
using Quark.Extensions.DependencyInjection;

builder.Services.UseQuarkClient(...);
```

**After:**
```csharp
using Quark.Client.DependencyInjection;  // Changed!

builder.Services.UseQuarkClient(...);  // Same API
```

### Key Changes

1. Import namespace changed from `Quark.Extensions.DependencyInjection` to `Quark.Client.DependencyInjection`
2. Client registration now uses `RedisClientClusterMembership` instead of `RedisClusterMembership`
3. No `QuarkSiloOptions` required for client-only scenarios

## Example: API Gateway

```csharp
using Quark.Client.DependencyInjection;

var builder = WebApplication.CreateSlimBuilder(args);

// Configure Quark client
builder.Services.UseQuarkClient(
    configure: options => options.ClientId = "api-gateway",
    clientBuilderConfigure: client =>
    {
        client.WithRedisClustering(connectionString: "localhost:6379");
        client.WithGrpcTransport();
    });

// Add your API controllers/endpoints
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
await app.RunAsync();
```

## Benefits

1. **Cleaner separation**: Client and silo concerns are fully separated
2. **Simpler dependencies**: Client apps don't need silo-specific packages
3. **Better semantics**: `RedisClientClusterMembership` clearly indicates read-only usage
4. **No runtime errors**: Client apps won't try to access `QuarkSiloOptions`
5. **Smaller deployment**: Client apps can be smaller without silo infrastructure

## See Also

- [Quark Client Documentation](../client/README.md)
- [Quark Silo Documentation](../hosting/README.md)
- [Example: Awesome Pizza Gateway](../../productExample/src/Quark.AwesomePizza.Gateway/Program.cs)
