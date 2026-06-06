# Adventure Sample — Design Spec

**Date:** 2026-06-06  
**Branch target:** `main`

## Overview

Port the Orleans *Adventure* MES sample to Quark to demonstrate real-world, multi-grain, two-process usage. Before writing the sample, close two small Orleans API compatibility gaps in `Quark.Core.Abstractions`. The sample then compiles with call sites identical to the Orleans original.

---

## Part 1 — Quark API Additions

### 1a. `Grain.RegisterGrainTimer` stateless overload

**File:** `src/Quark.Core.Abstractions/Grains/Grain.cs`

Add a convenience overload matching Orleans' most common timer pattern:

```csharp
protected IGrainTimer RegisterGrainTimer(
    Func<CancellationToken, Task> callback,
    TimeSpan dueTime,
    TimeSpan period)
```

Implementation delegates to the existing `RegisterGrainTimer<TState>` overload using a `null` state:

```csharp
=> RegisterGrainTimer<object?>(
    (_, ct) => callback(ct),
    null,
    new GrainTimerCreationOptions { DueTime = dueTime, Period = period });
```

This makes `this.RegisterGrainTimer(_ => Move(), dueTime, period)` compile without change.

### 1b. `GrainExtensions` static class

**File:** `src/Quark.Core.Abstractions/Grains/GrainExtensions.cs` (new)

Extension methods that allow callers to extract the primary key from any grain *reference* (proxy), matching Orleans' `GrainExtensions` API:

```csharp
public static class GrainExtensions
{
    public static Guid   GetPrimaryKey(this IGrainWithGuidKey grain)
    public static long   GetPrimaryKeyLong(this IGrainWithIntegerKey grain)
    public static string GetPrimaryKeyString(this IGrainWithStringKey grain)
}
```

Each method casts the grain reference to `IGrainProxy` and parses `.GrainId.Key`:

```csharp
public static Guid GetPrimaryKey(this IGrainWithGuidKey grain)
    => Guid.ParseExact(((IGrainProxy)grain).GrainId.Key, "N");

public static long GetPrimaryKeyLong(this IGrainWithIntegerKey grain)
    => long.Parse(((IGrainProxy)grain).GrainId.Key, CultureInfo.InvariantCulture);

public static string GetPrimaryKeyString(this IGrainWithStringKey grain)
    => ((IGrainProxy)grain).GrainId.Key;
```

This allows `_roomGrain.GetPrimaryKey() != room.GetPrimaryKey()` to compile without change.

---

## Part 2 — `samples/Adventure/` Sample

### Project structure

```
samples/Adventure/
├── Adventure.slnx                    ← standalone solution
├── README.md                         ← run instructions + adaptation notes
├── AdventureMap.json                 ← game data (copied from Orleans sample)
├── Adventure.GrainInterfaces/        ← grain interfaces + serializable models
│   ├── IPlayerGrain.cs
│   ├── IRoomGrain.cs
│   ├── IMonsterGrain.cs
│   └── Models.cs                     ← PlayerInfo, MonsterInfo, Thing, RoomInfo, MapInfo, CategoryInfo
├── Adventure.Grains/                 ← grain implementations
│   ├── PlayerGrain.cs
│   ├── RoomGrain.cs
│   └── MonsterGrain.cs
├── Adventure.Server/                 ← silo + TCP gateway + map loader
│   ├── Program.cs
│   └── AdventureGame.cs
└── Adventure.Client/                 ← TCP gateway client + CLI loop
    └── Program.cs
```

All four projects reference Quark source via project references (no NuGet).

### `Adventure.GrainInterfaces`

Grain interfaces are identical to Orleans original. Data records require `[property: Id(n)]` on each primary constructor parameter for Quark's serializer generator:

```csharp
[GenerateSerializer, Immutable]
public record class PlayerInfo(
    [property: Id(0)] Guid Key,
    [property: Id(1)] string? Name);
```

All other records (`MonsterInfo`, `Thing`, `RoomInfo`, `MapInfo`, `CategoryInfo`) follow the same pattern.

### `Adventure.Grains`

Implementations are identical to the Orleans originals with one structural difference: the `MonsterGrain` timer call uses the new stateless overload added in Part 1:

```csharp
// Same call site as Orleans — works via the new overload
_timer = this.RegisterGrainTimer(
    _ => Move(),
    TimeSpan.FromSeconds(150),
    TimeSpan.FromMinutes(150));
```

`RoomGrain` and `PlayerGrain` are unchanged.

Key extraction on grain references in `MonsterGrain.Kill`:

```csharp
// Same call site as Orleans — works via GrainExtensions added in Part 1
_roomGrain.GetPrimaryKey() != room.GetPrimaryKey()
```

### `Adventure.Server`

```csharp
Host.CreateDefaultBuilder(args)
    .UseQuark(silo =>
    {
        silo.Services.AddQuarkRuntime();
        silo.UseLocalhostClustering();   // ISiloBuilder method; also starts TCP gateway on port 30000
        // grain registrations (AddGrain, AddGrainMethodInvoker, AddGrainActivatorFactory)
    })
    .Build();
```

`AdventureGame.cs` loads `AdventureMap.json` via `System.Text.Json` (no Newtonsoft dependency). Logic is identical to the Orleans original.

### `Adventure.Client`

```csharp
Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client =>
    {
        client.UseLocalhostGateway();    // IClientBuilder method; sets up TcpGatewayClusterClient
        // proxy registrations (AddGrainProxy)
    })
    .Build();
```

CLI loop is identical to the Orleans original.

### Adaptation summary (documented in README)

| Orleans | Quark | Notes |
|---------|-------|-------|
| `UseOrleans(...)` | `UseQuark(...)` | Naming only |
| `UseOrleansClient(...)` | `UseQuarkClient(...)` | Naming only |
| `[GenerateSerializer]` on records | `[property: Id(n)]` on each param | Quark requires explicit IDs |
| `RegisterGrainTimer(cb, due, period)` | Same — new overload added | No change at call site |
| `grain.GetPrimaryKey()` on ref | Same — `GrainExtensions` added | No change at call site |
| `Newtonsoft.Json` | `System.Text.Json` | Avoids extra dependency |

---

## Testing

- `Adventure.Server` and `Adventure.Client` compile cleanly with `dotnet build`.
- Manual smoke test: run server, run client, play several commands (go, look, take, kill, inv).
- No automated tests — the sample is a runnable demonstration, not a test project.

---

## Out of scope

- Persistence (grains use in-memory state only, matching the Orleans original)
- Remote multi-machine clustering
- The sample is not added to the CI test matrix
