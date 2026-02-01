# Quark.Client.DependencyInjection

Dependency injection extensions for configuring Quark cluster clients with Microsoft.Extensions.DependencyInjection. Client-only, lightweight, no silo hosting.

## Overview

This package provides DI extensions for applications that need to **call actors** but don't need to **host actors**. Perfect for API gateways, web apps, console tools, and services that consume actors hosted elsewhere.

## Installation

```bash
dotnet add package Quark.Client.DependencyInjection
```

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Add Quark cluster client
builder.Services.UseQuarkClient(
    configure: options => options.ClientId = "my-client",
    clientBuilderConfigure: client =>
    {
        client.WithRedisClustering(connectionString: "localhost:6379");
        client.WithGrpcTransport();
    });

var app = builder.Build();
await app.RunAsync();
```

## Features

- **🚀 Lightweight**: No actor hosting overhead
- **📡 Cluster Discovery**: Redis-based membership for finding silos
- **🔌 gRPC Transport**: Efficient remote communication
- **❤️ Health Checks**: Monitor cluster connectivity
- **🔄 Auto-Reconnect**: Automatic retry logic
- **🎯 AOT Ready**: Full Native AOT compatibility

## What's Included

### Extensions

- `UseQuarkClient()` - Register and auto-start cluster client
- `AddQuarkClient()` - Register cluster client only (manual start)
- `WithRedisClustering()` - Add Redis-based cluster membership
- `WithGrpcTransport()` - Add gRPC transport layer
- `AddHealthCheck()` - Add client health check

### Services

- `RedisClientClusterMembership` - Read-only cluster membership
- `StartClusterClientHostedService` - Auto-connect hosted service

## Usage

### Basic Client Setup

```csharp
builder.Services.UseQuarkClient(
    configure: options =>
    {
        options.ClientId = "api-gateway";
        options.MaxRetries = 3;
        options.RetryDelay = TimeSpan.FromSeconds(1);
        options.RequestTimeout = TimeSpan.FromSeconds(30);
    },
    clientBuilderConfigure: client =>
    {
        client.WithRedisClustering(connectionString: redisHost);
        client.WithGrpcTransport(enableChannelPooling: true);
        client.AddHealthCheck();
    });
```

### Calling Actors

```csharp
public class MyService
{
    private readonly IClusterClient _client;

    public MyService(IClusterClient client)
    {
        _client = client;
    }

    public async Task DoWorkAsync()
    {
        // Get a typed proxy for an actor
        var counter = _client.GetActor<ICounterActor>("counter-1");
        
        // Call methods on the actor
        await counter.IncrementAsync();
        var value = await counter.GetValueAsync();
    }
}
```

## Key Differences from Silo Extensions

| Feature | Client Package | Silo Package |
|---------|---------------|--------------|
| Hosts actors | ❌ No | ✅ Yes |
| Calls actors | ✅ Yes | ✅ Yes |
| Registers in cluster | ❌ No | ✅ Yes |
| Sends heartbeats | ❌ No | ✅ Yes |
| Membership | Read-only | Read-write |
| Package | `Quark.Client.DependencyInjection` | `Quark.Extensions.DependencyInjection` |

## Architecture

### RedisClientClusterMembership

Client-only membership implementation:

```csharp
// ✅ Supported (read-only operations)
await membership.GetActiveSilosAsync();
await membership.GetSiloAsync(siloId);
var targetSilo = membership.GetActorSilo(actorId, actorType);

// ❌ Not supported (throws NotSupportedException)
await membership.RegisterSiloAsync(siloInfo);
await membership.UnregisterSiloAsync();
await membership.UpdateHeartbeatAsync();
```

### Cluster Discovery Flow

1. Client starts and connects to Redis
2. Subscribes to membership changes
3. Discovers active silos from Redis
4. Routes actor calls via consistent hashing
5. Uses gRPC to communicate with target silos

## Examples

See `examples/Quark.Examples.ClientOnly/` for a complete working example.

## Documentation

- [Complete Setup Guide](../../docs/CLIENT_ONLY_SETUP.md)
- [Quark Client Documentation](../Quark.Client/README.md)
- [Example: API Gateway](../../productExample/src/Quark.AwesomePizza.Gateway/)

## License

This project is licensed under the MIT License.
