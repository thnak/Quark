# Adventure Sample

A two-process adventure game demonstrating Quark's TCP gateway, grain timers, and grain-to-grain calls. Ported from the [Orleans Adventure sample](https://github.com/dotnet/orleans/tree/main/samples/Adventure).

## Running

Open two terminals from the repo root.

**Terminal 1 — Server:**
```bash
dotnet run --project samples/Adventure/Adventure.Server
```

**Terminal 2 — Client:**
```bash
dotnet run --project samples/Adventure/Adventure.Client
```

Enter your name, then use commands: `look`, `go north/south/east/west`, `take <item>`, `drop <item>`, `inv`, `kill <monster>`, `quit`.

## Orleans → Quark Adaptation Notes

| Orleans | Quark | Notes |
|---------|-------|-------|
| `UseOrleans(...)` | `UseQuark(...)` | Naming only |
| `UseOrleansClient(...)` | `UseQuarkClient(...)` | Naming only |
| `[GenerateSerializer]` records with `[property: Id(n)]` | Plain mutable classes — no annotations needed | `GrainMessageSerializer` handles only primitives over TCP; complex types remain in-process |
| `RegisterGrainTimer(cb, due, period)` | Same — new stateless overload added | No change at call site |
| `grain.GetPrimaryKey()` on a reference | Same — `GrainExtensions` static class added | No change at call site |
| `Newtonsoft.Json` | `System.Text.Json` | Avoids extra dependency |
| In-process client uses same grain factory | `UseQuarkClient(...)` on server host | Local cluster client provides `IGrainFactory` |
