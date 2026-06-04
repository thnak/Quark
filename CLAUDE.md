# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

Quark is a **Native AOT-first, Orleans-compatible distributed actor framework** for .NET 10. It follows the Orleans mental model (Grain, Silo, Client, Placement, Persistence) with three API compatibility tiers:
- **Drop-in** — same attribute/interface names and signatures as Orleans
- **Minor-change** — same concept, different DI wiring
- **Quark-native** — new concepts without direct Orleans equivalents

## Commands

```bash
# Build entire solution
dotnet build Quark.slnx

# Run all tests
dotnet test Quark.slnx

# Run a single test project
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj
dotnet test tests/Quark.Tests.CodeGenerator/Quark.Tests.CodeGenerator.csproj
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj

# Run a single test by name
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~GrainCallIntegrationTests"

# Native AOT smoke build (Linux)
dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -f net10.0 -c Release -r linux-x64 /p:PublishAot=true
```

.NET SDK version is pinned to `10.0.201` in `global.json`. Package versions are managed centrally in `Directory.Packages.props`; do not add `Version=` attributes to individual `<PackageReference>` elements.

## Architecture

### Package layout

| Package | Role |
|---|---|
| `Quark.Core.Abstractions` | `GrainId`, `GrainType`, `IGrain`, `Grain` base, key-typed grain interfaces, lifecycle, placement attributes, `IGrainFactory`, `IClusterClient`, `IGrainContext` |
| `Quark.Serialization.Abstractions` | `IFieldCodec<T>`, `IDeepCopier<T>`, `IGeneralizedCodec/Copier`, `CodecWriter`/`CodecReader` (ZigZag+LEB128), `[GenerateSerializer]`/`[Id]`/`[Alias]` |
| `Quark.Transport.Abstractions` | `ITransport`, `ITransportListener`, `ITransportConnection` (IDuplexPipe), `MessageEnvelope` |
| `Quark.Core` | `ISiloBuilder`, `IClientBuilder`, `UseQuark()`/`UseQuarkClient()` host-builder extensions |
| `Quark.Runtime` | Silo-side runtime: `LifecycleSubject`, `GrainTypeRegistry`, `GrainActivationTable`, `LocalGrainCallInvoker`, `SiloHostedService`, message pump/dispatcher |
| `Quark.Client` | `LocalClusterClient`, `LocalGrainFactory`, proxy factory/interface registries |
| `Quark.Serialization` | 18 primitive codecs, `CodecProvider`, `QuarkSerializer`, serialization DI |
| `Quark.Transport.Tcp` | `TcpTransport`/`TcpTransportListener`/`TcpTransportConnection` (System.IO.Pipelines) |
| `Quark.Persistence.Abstractions` | `IGrainStorage`, `IPersistentGrain<TState>`, `IStorage<TState>`, `GrainState<T>` |
| `Quark.Persistence.InMemory` | In-memory `IGrainStorage` provider |
| `Quark.Persistence.Redis` | Redis-backed `IGrainStorage` provider (StackExchange.Redis) |
| `Quark.CodeGenerator` | Roslyn incremental generators: `GrainProxyGenerator`, `GrainActivatorGenerator`, `SerializerGenerator` |
| `Quark.Analyzers` | AOT-safety Roslyn analyzers (QRK0001–QRK0003: dynamic type, Assembly.Load, ISerializable) |
| `Quark.Testing` | `TestCluster`/`TestSilo`/`TestClient` in-process multi-silo test harness |

### Call flow for a grain method invocation

```
IClusterClient.GetGrain<IMyGrain>(key)
  → LocalGrainFactory → generated GrainProxy (holds GrainId + IGrainCallInvoker)
  → proxy.MethodAsync() → LocalGrainCallInvoker.InvokeAsync()
  → GrainActivationTable (find or create GrainActivation)
  → GrainActivation.Channel<Func<Task>> (serialises concurrent calls)
  → IGrainMethodInvoker.InvokeAsync() (generated per grain type)
  → concrete Grain method
```

### DI registration pattern

Grains, method invokers, and proxies use a deferred-registration pattern: `AddGrain<T>()`, `AddGrainMethodInvoker<TGrain,TInvoker>()`, and `AddGrainProxy<TInterface,TProxy>()` add sentinel `IGrainRegistration`/`IGrainMethodInvokerRegistration`/`IProxyRegistration` singletons that are resolved and applied by `SiloHostedService.StartAsync` and `ClientStartupService`. This avoids any reflection or assembly scanning at startup.

Typical silo+client wiring:

```csharp
builder.UseQuark(silo =>
{
    silo.Services.AddQuarkRuntime();
    silo.Services.AddGrain<MyGrain>();
    silo.Services.AddGrainMethodInvoker<MyGrain, MyGrainMethodInvoker>(); // normally code-gen'd
    silo.Services.AddGrainActivatorFactory<MyGrainActivatorFactory>();    // normally code-gen'd
});
builder.UseQuarkClient(client =>
{
    client.Services.AddLocalClusterClient();
    client.Services.AddGrainProxy<IMyGrain, MyGrainProxy>();              // normally code-gen'd
});
```

## Source generators

`Quark.CodeGenerator` ships three Roslyn incremental generators:

- **`GrainProxyGenerator`** — for every `interface` that inherits `IGrain`, emits `{InterfaceName[1..]}Proxy` implementing the interface and routing calls through `IGrainCallInvoker`.
- **`GrainActivatorGenerator`** — for every concrete `Grain` subclass, emits `{ClassName}ActivatorFactory : IGrainActivatorFactory` that constructs the grain by resolving constructor parameters from `IServiceProvider` (AOT-safe, no reflection).
- **`SerializerGenerator`** — for every type annotated `[GenerateSerializer]`, emits `IFieldCodec<T>` + `IDeepCopier<T>` using `[Id(uint)]`-tagged members.

In test projects, hand-write the invoker/proxy (see `tests/Quark.Tests.Unit/Integration/`) instead of relying on generators.

## Serialization conventions

- Apply `[GenerateSerializer]` to any type that crosses a grain call boundary.
- Tag each serialized member with `[Id(uint)]` — IDs must be stable across versions.
- `[Alias("name")]` provides a stable string alias for type-level versioning.
- `CodecWriter`/`CodecReader` use ZigZag + LEB128 variable-length encoding.

## Placement strategies

Declare on the grain class; the `AttributePlacementStrategyResolver` picks the strategy at activation time:

| Attribute | Behaviour |
|---|---|
| `[RandomPlacement]` (default) | Activate on any available silo |
| `[PreferLocalPlacement]` | Prefer the silo handling the call |
| `[HashBasedPlacement]` | Deterministic silo via key hash |
| `[LocalPlacement]` | Must activate on the local silo |
| `[StatelessWorker]` | Multiple activations allowed per silo |

## Persistence

Grains that need durable state implement `IPersistentGrain<TState>` (or inherit `Grain<TState>` from `Quark.Persistence.Abstractions`). The `IGrainStorage` provider is injected; swap between in-memory and Redis via DI:

```csharp
services.AddInMemoryGrainStorage();     // Quark.Persistence.InMemory
services.AddRedisGrainStorage(options => { options.ConnectionString = "..."; });  // Quark.Persistence.Redis
```

## AOT / trim constraints

Every production package has `IsTrimmable=true` and `EnableAotAnalyzer=true` (set in `Directory.Build.props`). New code must:

1. Prefer source generation over runtime reflection.
2. Annotate unavoidable dynamic calls with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`.
3. Guard JIT-only paths with `RuntimeFeature.IsDynamicCodeSupported`.
4. Use `[UnsafeAccessor]` instead of `DynamicMethod` for private-member access.
5. Never introduce `ISerializable`-based patterns (triggers QRK0003).
6. Use explicit provider registration; avoid assembly-scanning discovery.

## Testing

Use `Quark.Testing.Harness.TestCluster` for in-process integration tests:

```csharp
await using var cluster = await TestCluster.CreateAsync(options =>
{
    options.ConfigureSiloServices = services => services.AddGrain<MyGrain>();
    options.ConfigureClientServices = services => services.AddGrainProxy<IMyGrain, MyGrainProxy>();
});
var grain = cluster.Client.GetGrain<IMyGrain>("key");
```

Tests requiring Redis use Testcontainers (`Testcontainers` package, `Quark.Tests.Integration`). Skip integration tests when infrastructure is unavailable via `[Trait("category","integration")]`.
