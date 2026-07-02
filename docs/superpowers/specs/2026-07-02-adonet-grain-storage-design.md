**Issue:** #40 — ADO.NET / PostgreSQL grain storage with ETag concurrency; supersedes #105  
**Date:** 2026-07-02  
**Status:** Planned — design also posted as a comment on the issue

## Final design — ADO.NET / PostgreSQL grain storage provider with ETag optimistic concurrency

New package `Quark.Persistence.AdoNet` implementing `IGrainStorage`, PostgreSQL (Npgsql) first, with true ETag-based optimistic concurrency. This design also retrofits `Quark.Persistence.Redis` so both providers enforce ETag identically, and adds `InconsistentStateException` to `Quark.Persistence.Abstractions`.

### Goals / Non-goals

**Goals**
- A durable relational `IGrainStorage` provider that plugs into the existing `[PersistentState("slot","provider")]`, `IPersistentState<T>`, `IPersistentActivationMemory<T>`, and `IStorage<T>` paths exactly like the Redis provider.
- Real optimistic concurrency: conditional `UPDATE`/`DELETE` keyed on the stored ETag; a mismatch throws `InconsistentStateException` (Orleans semantics). This is currently absent everywhere in Quark.
- Make ETag enforcement **uniform** across providers by retrofitting Redis to the same contract, so grain code sees identical concurrency behavior regardless of backend.
- AOT/trim-safe: raw `DbCommand` with positional parameters, no EF/ORM/reflection mapping, payloads via the existing binary `ISerializer`.
- Idempotent, opt-in schema deployment plus a shippable DDL script for DBA-managed environments.

**Non-goals**
- No per-vendor packages in this change (SQL Server / MySQL). The package is built to add dialects later; only the PostgreSQL dialect ships now.
- No EF Core, no `DbProviderFactories` invariant-name registry (both are trim-hostile; see AOT notes).
- No change to the `IGrainStorage` / `GrainState<T>` contract — the ETag field already exists.
- No new state pattern; this is a storage provider only.
- Reminder/clustering tables are out of scope (grain state only).

### Proposed API & options

New package `Quark.Persistence.AdoNet`.

```csharp
namespace Quark.Persistence.AdoNet;

public enum AdoNetDialect
{
    PostgreSql = 0, // Npgsql — the only dialect shipped now
    // SqlServer, MySql — reserved for later
}

public sealed class AdoNetStorageOptions
{
    /// <summary>Connection string. Used to build a DbDataSource for the selected dialect
    /// when <see cref="DataSource"/> is not supplied.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Selects the SQL dialect (statement syntax + parameter style). PostgreSQL only for now.</summary>
    public AdoNetDialect Dialect { get; set; } = AdoNetDialect.PostgreSql;

    /// <summary>Physical table name. Must be a valid, trusted identifier (never user input).</summary>
    public string TableName { get; set; } = "quark_grain_state";

    /// <summary>When true, issues an idempotent CREATE TABLE IF NOT EXISTS once before first use.
    /// Set false in production and deploy the shipped DDL script via your migration tooling.</summary>
    public bool AutoDeploySchema { get; set; } = true;

    /// <summary>Escape hatch: supply a pre-built DbDataSource (any ADO.NET provider) to bypass
    /// the built-in Npgsql construction. When set, <see cref="ConnectionString"/> is ignored.</summary>
    public System.Data.Common.DbDataSource? DataSource { get; set; }
}
```

DI extensions (mirroring `RedisGrainStorageServiceCollectionExtensions`):

```csharp
public static class AdoNetGrainStorageServiceCollectionExtensions
{
    // Default (unnamed) provider — resolves for [PersistentState("slot")] / [PersistentState("slot","Default")].
    public static IServiceCollection AddAdoNetGrainStorage(
        this IServiceCollection services, Action<AdoNetStorageOptions> configure);

    // Named provider — resolves for [PersistentState("slot","adonet")].
    public static IServiceCollection AddKeyedAdoNetGrainStorage(
        this IServiceCollection services, string name, Action<AdoNetStorageOptions> configure);
}
```

> Note: the draft in #40 exposed a `Serializer` property on the options object. Removed — the provider injects `ISerializer` from DI exactly like `RedisGrainStorage`, keeping serializer configuration in one place. The draft's `DbProviderFactory`-by-`Invariant` selection is also replaced by `DbDataSource` injection (see AOT notes); `Dialect` now selects only SQL text, not the factory.

Internal seam (enables unit tests without a live DB, mirroring `IRedisStorageConnection`):

```csharp
internal readonly record struct AdoNetStorageKey(string GrainType, string GrainKey, string StateName);
internal readonly record struct AdoNetStorageRecord(byte[] Payload, string ETag);

internal interface IAdoNetStorageExecutor
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<AdoNetStorageRecord?> ReadAsync(AdoNetStorageKey key, CancellationToken ct = default);
    /// <returns>rows affected (0 == ETag conflict for a conditional write).</returns>
    Task<int> WriteAsync(AdoNetStorageKey key, byte[] payload, string expectedEtag, string newEtag, CancellationToken ct = default);
    /// <returns>rows affected (0 == ETag conflict for a conditional delete).</returns>
    Task<int> DeleteAsync(AdoNetStorageKey key, string expectedEtag, CancellationToken ct = default);
}
```

`AdoNetGrainStorage : IGrainStorage` holds `IAdoNetStorageExecutor` + `ISerializer` and mirrors `RedisGrainStorage`'s read/write/clear shape. `NpgsqlStorageExecutor : IAdoNetStorageExecutor` is the concrete default (built on `NpgsqlDataSource`). A thin `AdoNetStorage<TState> : IStorage<TState>` facade mirrors `RedisStorage<TState>` and is registered for `IStorage<>`.

Key layout matches Redis exactly:
`AdoNetStorageKey.GrainType = grainId.Type.Value`, `.GrainKey = grainId.Key`, `.StateName = stateName`. (The Redis provider additionally appends the CLR type name to the key; the relational schema instead keys on `(grain_type, grain_key, state_name)` — one state type per slot, which is the actual invariant.)

### Schema & SQL (PostgreSQL first)

**DDL** (shipped as `Quark.Persistence.AdoNet/Schema/PostgreSql.sql`; also issued verbatim by `EnsureSchemaAsync` when `AutoDeploySchema=true`):

```sql
CREATE TABLE IF NOT EXISTS quark_grain_state (
    grain_type   text        NOT NULL,
    grain_key    text        NOT NULL,
    state_name   text        NOT NULL,
    payload      bytea       NOT NULL,
    etag         text        NOT NULL,
    modified_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_quark_grain_state PRIMARY KEY (grain_type, grain_key, state_name)
);
```

**READ**

```sql
SELECT payload, etag
FROM quark_grain_state
WHERE grain_type = $1 AND grain_key = $2 AND state_name = $3;
```
No row → `grainState.State = new()`, `RecordExists=false`, `ETag=""`. Row → deserialize payload, `RecordExists=true`, `ETag=<stored>`.

**WRITE** — single atomic CAS upsert. `$5` = newly generated ETag, `$6` = expected (incoming `grainState.ETag`, `""` when the caller believes the record is new):

```sql
INSERT INTO quark_grain_state AS s (grain_type, grain_key, state_name, payload, etag, modified_at)
VALUES ($1, $2, $3, $4, $5, now())
ON CONFLICT (grain_type, grain_key, state_name)
DO UPDATE SET payload = EXCLUDED.payload, etag = EXCLUDED.etag, modified_at = now()
WHERE s.etag = $6;
```
Rows-affected semantics:
- No existing row → `INSERT` path, 1 row. (If `$6` is a stale non-empty ETag but no row exists, the insert still succeeds — first write wins; identical to Orleans' insert-on-empty behavior.)
- Existing row, `s.etag = $6` → `UPDATE` path, 1 row.
- Existing row, `s.etag <> $6` (including the "caller thought it was new" case where `$6=""`) → `WHERE` fails, **0 rows** → throw `InconsistentStateException`.

On success write back `grainState.ETag = $5`, `RecordExists = true`.

**CLEAR** — conditional on ETag when one is known:

```sql
DELETE FROM quark_grain_state
WHERE grain_type = $1 AND grain_key = $2 AND state_name = $3 AND etag = $4;
```
`$4` = incoming `grainState.ETag`. If `ETag != ""` and 0 rows affected → the row exists with a different ETag → throw `InconsistentStateException`. If `ETag == ""`, treat 0 rows as a no-op (nothing to clear). After success: `State = new()`, `RecordExists=false`, `ETag=""`.

All statements use positional `$n` parameters bound as `DbParameter` (`bytea` ↔ `byte[]`, `text` ↔ `string`). `TableName` is interpolated into the statement text once at executor construction (trusted identifier, never request-derived).

### DI & provider registration

Default provider:
```csharp
public static IServiceCollection AddAdoNetGrainStorage(
    this IServiceCollection services, Action<AdoNetStorageOptions> configure)
{
    services.AddOptions<AdoNetStorageOptions>();
    services.Configure(configure);
    services.TryAddSingleton<IAdoNetStorageExecutor>(sp =>
        NpgsqlStorageExecutor.Create(sp.GetRequiredService<IOptions<AdoNetStorageOptions>>().Value));
    services.TryAddSingleton<IGrainStorage, AdoNetGrainStorage>();
    services.TryAddSingleton(typeof(IStorage<>), typeof(AdoNetStorage<>));
    return services;
}
```

Named provider (frozen per-name options + keyed executor, exactly like `AddKeyedRedisGrainStorage`):
```csharp
public static IServiceCollection AddKeyedAdoNetGrainStorage(
    this IServiceCollection services, string name, Action<AdoNetStorageOptions> configure)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    var options = new AdoNetStorageOptions();
    configure(options);
    var frozen = Options.Create(options);
    services.AddKeyedSingleton<IAdoNetStorageExecutor>(name, (_, _) => NpgsqlStorageExecutor.Create(options));
    services.AddKeyedSingleton<IGrainStorage>(name, (sp, _) =>
        new AdoNetGrainStorage(
            sp.GetRequiredKeyedService<IAdoNetStorageExecutor>(name),
            sp.GetRequiredService<ISerializer>(),
            frozen));
    return services;
}
```

This is exactly what `BehaviorRegistrationGenerator` expects: default slots resolve `GetRequiredService<IGrainStorage>()`, named slots resolve `GetRequiredKeyedService<IGrainStorage>("adonet")` (see `BehaviorRegistrationGenerator.cs:398`). No code-generator change is required.

Schema deployment: when `AutoDeploySchema=true`, `NpgsqlStorageExecutor` runs `EnsureSchemaAsync` once, lazily, guarded by a `SemaphoreSlim` + a `volatile bool`, before the first read/write/clear. In production, set `AutoDeploySchema=false` and apply the shipped `.sql`.

### ETag semantics & Redis retrofit

**What Redis does today** (`RedisGrainStorage.cs:57-77`, `RedisStorageConnection.cs:47-59`): `WriteStateAsync` always generates a fresh `Guid.NewGuid().ToString("N")`, then calls `HashSetAsync` **unconditionally** — the incoming `grainState.ETag` is never read and never compared. `ClearStateAsync` calls `KeyDeleteAsync` unconditionally. Reads do return the stored ETag. Net effect: Redis is **last-writer-wins**; it stores/returns an ETag but enforces nothing.

**Target contract (both providers identical):**
- Read: load stored ETag into `grainState.ETag`; `RecordExists` reflects presence.
- Write: CAS against `grainState.ETag`; mismatch → `InconsistentStateException`; on success generate a new ETag and write it back.
- Clear: conditional on `grainState.ETag` when non-empty; mismatch → `InconsistentStateException`.
- Empty incoming ETag means "I expect no prior record"; first write wins.

**Where enforcement actually bites:** `PersistentState<TState>` (`PersistentState.cs`) keeps one `GrainState<TState>` instance, so ETag round-trips **within a single call** that reads-then-writes, and across writes on a shell-cached `IPersistentActivationMemory<T>`. Because `[PersistentState]` scopes are per-call (a fresh `PersistentState<T>` per activation call — see MEMORY: "IPersistentState is per-call"), a behavior that writes without reading first sends `ETag=""` and gets first-write-wins insert semantics — this is the intended Orleans behavior and must be documented in the persistence wiki. The `IStorage<TState>` convenience facade (`RedisStorage<T>` / `AdoNetStorage<T>`) constructs a fresh empty `GrainState` per call and therefore always writes with `ETag=""` (last-writer-wins); this is unchanged existing behavior and is called out as a known limitation of that facade.

**`InconsistentStateException`** — add to `Quark.Persistence.Abstractions`:
```csharp
namespace Quark.Persistence.Abstractions;

public sealed class InconsistentStateException : Exception
{
    public InconsistentStateException(string message, string storedEtag, string expectedEtag)
        : base(message) { StoredEtag = storedEtag; ExpectedEtag = expectedEtag; }
    public InconsistentStateException(string message, string storedEtag, string expectedEtag, Exception inner)
        : base(message, inner) { StoredEtag = storedEtag; ExpectedEtag = expectedEtag; }

    /// <summary>ETag currently persisted (empty if the record was expected but absent).</summary>
    public string StoredEtag { get; }
    /// <summary>ETag the caller expected (the value in GrainState&lt;T&gt;.ETag).</summary>
    public string ExpectedEtag { get; }
}
```

**Redis retrofit** (same PR, so both ship consistent): make the CAS atomic with a Lua script so the check-and-set is a single server round trip.
- Extend `IRedisStorageConnection`: `WriteAsync(key, record, expectedEtag, ct)` → returns `bool committed`; `DeleteAsync(key, expectedEtag, ct)` → returns `bool committed`.
- `RedisStorageConnection.WriteAsync` runs (via `ScriptEvaluateAsync`):
  ```lua
  local cur = redis.call('HGET', KEYS[1], 'etag')
  if (cur == false and ARGV[1] == '') or (cur == ARGV[1]) then
    redis.call('HSET', KEYS[1], 'payload', ARGV[2], 'etag', ARGV[3]); return 1
  else return 0 end
  ```
  (`ARGV[1]`=expected, `ARGV[2]`=payload, `ARGV[3]`=new etag). Delete uses the analogous check-then-DEL script.
- `RedisGrainStorage` passes `grainState.ETag` as expected and throws `InconsistentStateException` when the script returns 0. This is a behavior change for Redis; note it in the changelog. Existing `RedisStorageTests` (fake connection) must be updated to the new seam signatures.

### AOT & trim notes

- **No `DbProviderFactories`.** The draft's `DbProviderFactory`-keyed-by-invariant selection relies on `DbProviderFactories.GetFactory(string)`, a reflection-backed name registry — trim-hostile. Instead the provider is built on `System.Data.Common.DbDataSource` (abstract, .NET 7+). `NpgsqlDataSource : DbDataSource` is constructed directly from the connection string for the PostgreSQL dialect, or supplied by the caller via `AdoNetStorageOptions.DataSource` for any other ADO.NET provider. `Dialect` selects only the SQL text.
- **Raw `DbCommand` only** — no EF Core, no ORM, no reflection-based row mapping. Columns read positionally: `GetFieldValue<byte[]>(0)`, `GetString(1)`. Parameters bound positionally via `command.CreateParameter()`.
- **Payloads via `ISerializer`** (binary `QuarkSerializer`), identical to Redis — codec-driven, no `BinaryFormatter`, no `ISerializable` (would trip QRK0003).
- `Quark.Persistence.AdoNet.csproj`: `TargetFrameworks net9.0;net10.0`; `IsTrimmable`/`EnableTrimAnalyzer`/`EnableAotAnalyzer` come automatically from `Directory.Build.props`. `ProjectReference` to `Quark.Persistence.Abstractions` + `Quark.Serialization`; `PackageReference` to `Npgsql`, `Microsoft.Extensions.DependencyInjection[.Abstractions]`, `Microsoft.Extensions.Options` — no `Version=` (central `Directory.Packages.props`).
- **`Directory.Packages.props`**: add `Npgsql` (9.x — the modern Npgsql line publishes trim/AOT annotations) and, for tests, `Testcontainers.PostgreSql`. Run the Native-AOT smoke publish against a sample using the provider and confirm zero new trim warnings; if Npgsql surfaces any, isolate them behind the executor and annotate, do not suppress globally.

### Test plan

**Unit** (`tests/Quark.Tests.Unit/Persistence/AdoNetStorageTests.cs`, no DB — mirrors `RedisStorageTests` with a fake seam):
- `FakeAdoNetStorageExecutor : IAdoNetStorageExecutor` backed by a `Dictionary<AdoNetStorageKey, AdoNetStorageRecord>` implementing the exact rows-affected/CAS semantics.
- Write→Read round-trips state; `Read` returns a deep copy (not same reference); ETag populated on read.
- Clear removes the record; subsequent read returns default state, `RecordExists=false`.
- Write with matching ETag succeeds and rotates the ETag.
- Write with stale ETag → `InconsistentStateException` (assert `StoredEtag`/`ExpectedEtag`).
- Clear with stale ETag → `InconsistentStateException`; Clear with empty ETag on absent row → no-op.
- Named provider resolves via `GetRequiredKeyedService<IGrainStorage>("adonet")`.

**Redis retrofit unit tests** (extend `RedisStorageTests`): update `FakeRedisStorageConnection` to the new signatures; add stale-ETag → `InconsistentStateException` and empty-ETag-first-write cases so Redis and AdoNet share a behavioral test matrix.

**Integration** (`tests/Quark.Tests.Integration/AdoNetStorageIntegrationTests.cs`, Testcontainers.PostgreSql, `[Trait("category","integration")]`, skipped when Docker is unavailable — matches the repo's Testcontainers usage):
- `AutoDeploySchema` creates the table; read/write/clear round-trip against real Postgres.
- ETag conflict: two `GrainState` instances read the same row, both write; second write throws `InconsistentStateException`.
- Concurrent writers: N parallel CAS writes on one key — exactly one commits per generation, losers throw.
- `[PersistentState("slot","adonet")]` end-to-end through a `TestCluster`-style silo (add to `PersistentStateInjectionTests` coverage) resolving the keyed provider.
- Restart/reactivation: state written, provider re-read from a fresh executor returns the persisted value.

### Implementation checklist

- [ ] Add `InconsistentStateException` to `Quark.Persistence.Abstractions` (with `StoredEtag`/`ExpectedEtag`).
- [ ] Add `Npgsql` and `Testcontainers.PostgreSql` `PackageVersion` entries to `Directory.Packages.props`.
- [ ] New project `src/Quark.Persistence.AdoNet/Quark.Persistence.AdoNet.csproj` (`net9.0;net10.0`, refs Abstractions + Serialization + Npgsql); add to `Quark.slnx`.
- [ ] `AdoNetStorageOptions` + `AdoNetDialect`.
- [ ] Seam: `IAdoNetStorageExecutor`, `AdoNetStorageKey`, `AdoNetStorageRecord`.
- [ ] `ISqlDialect` (statement text for read/upsert-CAS/conditional-delete/DDL) + `PostgreSqlDialect`.
- [ ] `NpgsqlStorageExecutor : IAdoNetStorageExecutor` (DbDataSource, positional `DbParameter`, lazy `EnsureSchemaAsync` guard).
- [ ] `AdoNetGrainStorage : IGrainStorage` (read/write/clear + ETag CAS + throw on 0 rows + ETag rotation).
- [ ] `AdoNetStorage<TState> : IStorage<TState>` facade.
- [ ] `AdoNetGrainStorageServiceCollectionExtensions`: `AddAdoNetGrainStorage` + `AddKeyedAdoNetGrainStorage`.
- [ ] Shipped DDL `Schema/PostgreSql.sql`.
- [ ] Retrofit Redis: extend `IRedisStorageConnection` (expected-ETag params + bool result), Lua CAS in `RedisStorageConnection`, throw `InconsistentStateException` in `RedisGrainStorage`; update `RedisStorageTests` fake.
- [ ] Unit tests `AdoNetStorageTests` (fake seam) + Redis ETag cases.
- [ ] Integration tests `AdoNetStorageIntegrationTests` (Testcontainers.PostgreSql).
- [ ] AOT smoke publish with the provider referenced; confirm no new trim warnings.
- [ ] Docs: `wiki/Persistence.md` (add AdoNet provider + ETag/`InconsistentStateException` section, note `IStorage<T>` facade is last-writer-wins), `FEATURES.md` parity row, `quark-persistence` skill storage-provider list.

### Resolved design decisions

**From #40**
- *Provider selection mechanism.* **Decision:** inject `DbDataSource` (Npgsql-built from connection string by default, or caller-supplied), not `DbProviderFactory`-by-invariant. **Rationale:** `DbProviderFactories` is a reflection/name-registry mechanism that fights trimming; `DbDataSource` is the modern, AOT-clean ADO.NET entry point and doubles as the multi-vendor escape hatch.
- *Options carrying `ISerializer`.* **Decision:** drop it; inject `ISerializer` from DI like Redis. **Rationale:** one place to configure serialization, no ambient/duplicate config.
- *ETag write strategy.* **Decision:** single `INSERT … ON CONFLICT DO UPDATE … WHERE s.etag = $expected` CAS statement (one round trip, race-safe under Postgres row locking) rather than separate branchy insert/update. **Rationale:** atomic, fewer round trips, one SQL string to maintain; still expressible per-dialect.
- *Redis "follow-up".* **Decision:** do the Redis retrofit in the **same PR**, atomically via Lua. **Rationale:** the whole point is uniform ETag behavior across providers; shipping AdoNet-enforces-but-Redis-ignores would leave the concurrency contract backend-dependent.

**From #105**
- *Single ADO.NET-generic package vs per-vendor packages.* **Decision:** one generic `Quark.Persistence.AdoNet` package; PostgreSQL/Npgsql is the only bundled dialect now, with an internal `ISqlDialect` seam and a `DbDataSource` escape hatch for other ADO.NET providers. **Rationale:** avoids taking a hard dependency on every vendor driver while still delivering the requested Postgres path; per-vendor split (`Quark.Persistence.SqlServer`, …) can come later without breaking the API if a dialect needs its own driver package — only then does splitting earn its keep.
- *Schema/migration: auto-create vs migration script.* **Decision:** ship both — idempotent `CREATE TABLE IF NOT EXISTS` gated by `AutoDeploySchema` (default `true` for a frictionless dev start) **and** a checked-in `Schema/PostgreSql.sql` for DBA/CI-managed environments where the app has no DDL rights (`AutoDeploySchema=false`). **Rationale:** matches Orleans' shipped-script practice while keeping the getting-started path one line; no runtime migration framework (trim/AOT surface) is introduced.
