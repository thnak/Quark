**Issue:** #123 — unified per-grain-type policy; covers #115 (idle age) and #116 (max lifetime TTL)  
**Date:** 2026-07-02  
**Status:** Planned — design also posted as a comment on the issue

> **This design covers #115 (per-grain-type idle-collection age), #116 (grain max-lifetime / TTL forced deactivation), and #123 (per-grain-type concurrency limit) as a single per-grain-type policy surface.** They are one design: three fields on one `GrainTypePolicy` record, declared once per behavior class (attribute) or overridden per-type by ops (options). #115 and #116 should be closed as covered by this issue.

## Goals / Non-goals

### Goals
- One declarative, per-grain-type policy surface with three independent, individually-optional knobs:
  - **Idle-collection age** override (#115) — how long *this* type idles before collection.
  - **Max activation lifetime / TTL** (#116) — force periodic re-activation regardless of activity.
  - **Max activations per silo** for *this* type (#123) — stop one hot type exhausting the silo budget.
- Follow the existing declarative-attribute-on-behavior pattern (mirrors `[Reentrant]` / placement attributes), resolved through the **exact same mechanism** `AttributePlacementStrategyResolver` uses — reflection over the already-registered concrete `Type`, cached per `Type`, no assembly scanning.
- Provide an **options-based override** keyed by `GrainType` so operators can retune without recompiling; options win over the attribute per-field.
- Every field is nullable/sentinel = "inherit the silo-global default" (`SiloRuntimeOptions.GrainCollectionAge`, no TTL, `MaxActivations`).

### Non-goals
- **Mailbox priority (#109) is explicitly excluded.** It is a *scheduling/ordering* concern inside a single mailbox (which work item runs next), not a *lifecycle or capacity* concern. It requires replacing `GrainActivation`'s FIFO `Channel<MailboxWorkItem>` with a priority queue and touches the hot per-call path — coupling it into a lifecycle-policy record would join two unrelated subsystems. It gets its own design.
- No **cluster-wide** per-type activation limit — `MaxActivationsPerSilo` is per-silo, matching the existing silo-scoped `MaxActivations` semantics. Cluster-wide quotas need directory/placement coordination and are out of scope.
- No per-*grain-instance* (per-key) policy — this is per-**type** only. `DelayDeactivation` remains the per-instance escape hatch.

## Proposed API

### 1. The policy record (`Quark.Core.Abstractions.Grains`)

```csharp
namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Per-grain-type lifecycle and capacity policy. Every field is nullable; null means
///     "inherit the silo-global default" (SiloRuntimeOptions).
/// </summary>
public sealed record GrainTypePolicy
{
    /// <summary>Idle age before this type is collected. Null → SiloRuntimeOptions.GrainCollectionAge.</summary>
    public TimeSpan? IdleCollectionAge { get; init; }

    /// <summary>Max wall-clock lifetime of an activation before forced (drain-then-)deactivation,
    ///     regardless of activity. Null → no TTL (current behavior).</summary>
    public TimeSpan? MaxActivationLifetime { get; init; }

    /// <summary>Max concurrent activations of THIS type on one silo. Null → SiloRuntimeOptions.MaxActivations
    ///     (silo-wide only, no per-type cap).</summary>
    public int? MaxActivationsPerSilo { get; init; }

    /// <summary>The all-defaults policy (everything inherits).</summary>
    public static readonly GrainTypePolicy Default = new();
}
```

**Compatibility tier: Quark-native.** No Orleans equivalent (Orleans uses per-grain `[CollectionAgeLimit]` attribute for idle only; TTL and per-type concurrency have no Orleans analog). `[CollectionAgeLimit(Days=…, Hours=…, Minutes=…)]` can be added later as a **drop-in** alias that maps onto `IdleCollectionAge`.

### 2. The declarative attribute (`Quark.Core.Abstractions.Grains`, next to `ReentrantAttribute`)

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class GrainTypePolicyAttribute : Attribute
{
    // Attributes cannot hold TimeSpan? / int? constants, so numeric sentinels are used.
    // -1 (the default) means "unset — inherit". 0 is a meaningful value (e.g. disable).

    /// <summary>Idle-collection age in seconds. -1 = inherit.</summary>
    public double IdleCollectionAgeSeconds { get; init; } = -1;

    /// <summary>Max activation lifetime (TTL) in seconds. -1 = no TTL.</summary>
    public double MaxActivationLifetimeSeconds { get; init; } = -1;

    /// <summary>Max activations of this type per silo. -1 = inherit (no per-type cap).</summary>
    public int MaxActivationsPerSilo { get; init; } = -1;
}
```

Usage on a behavior:

```csharp
[GrainTypePolicy(IdleCollectionAgeSeconds = 300, MaxActivationLifetimeSeconds = 3600, MaxActivationsPerSilo = 5000)]
public sealed class SessionBehavior : ISessionGrain, IGrainBehavior { … }
```

### 3. The options override (`SiloRuntimeOptions`)

```csharp
// added to SiloRuntimeOptions
/// <summary>
///     Per-grain-type policy overrides keyed by GrainType.Value. An entry here overrides the
///     behavior's [GrainTypePolicy] attribute PER FIELD (a null field falls back to the attribute,
///     then to the silo-global default). Empty by default.
/// </summary>
public Dictionary<string, GrainTypePolicy> GrainTypePolicies { get; } = new(StringComparer.Ordinal);
```

### 4. The resolver (`Quark.Runtime`, mirrors `AttributePlacementStrategyResolver` exactly)

```csharp
namespace Quark.Runtime;

public interface IGrainTypePolicyProvider
{
    /// <summary>Resolves the effective policy for a grain type. Options override attribute per-field;
    ///     unset fields remain null (caller applies silo-global defaults).</summary>
    GrainTypePolicy GetPolicy(GrainType grainType, Type grainClass);
}

public sealed class AttributeGrainTypePolicyProvider : IGrainTypePolicyProvider
{
    // ConcurrentDictionary<Type, GrainTypePolicy> attribute cache — identical shape to
    // AttributePlacementStrategyResolver._cache. Reads Attribute on the registered concrete Type,
    // then merges the options dictionary on top (options-wins, per field).
}
```

### Precedence (per field, highest wins)

```
SiloRuntimeOptions.GrainTypePolicies[type].<field>   (ops override — wins)
    ↓ if null
[GrainTypePolicy] attribute on the behavior class    (author default)
    ↓ if unset (-1 sentinel)
silo-global default (GrainCollectionAge / no-TTL / MaxActivations)
```

Rationale: ops can retune a misbehaving type in production without a redeploy — the same reason placement has an options-based resolver override path. This is documented precedence: **options win over attribute.**

## Runtime integration (real anchors)

| Concern | Anchor | Change |
|---|---|---|
| Resolve policy once per activation | `LocalGrainCallInvoker.CreateActivationAsync` — already reads `behaviorType.IsDefined(typeof(ReentrantAttribute))` at `LocalGrainCallInvoker.cs:284` and has both `grainId.Type` and `behaviorType` | Call `_policyProvider.GetPolicy(grainId.Type, behaviorType)` right beside the reentrancy read; pass effective idle age + TTL into the `GrainActivation` ctor (`GrainActivation.cs:51`), pass the per-type limit into `GetOrCreateAsync`. |
| Store effective idle age + TTL on the shell | `GrainActivation` fields (`GrainActivation.cs:37-39`); it already tracks `_lastAccessedTicks` and `_activatedAtTicks` (Stopwatch, set in `MarkActive()` at `GrainActivation.cs:124`) | Add `TimeSpan? _idleCollectionAge` and `TimeSpan? _maxLifetime`; expose `IsIdleLongerThan` overload that uses the per-type age, and a new `HasExceededLifetime(now)`. |
| Idle age override (#115) | `GrainIdleCollector.CollectIdleGrains` (`GrainIdleCollector.cs:39`) — currently uses `_options.GrainCollectionAge` for all | Use `activation`'s effective idle age (per-type override ?? silo default). Because the effective age is stored on the activation, the collector never calls the provider on the scan path. |
| TTL forced deactivation (#116) | Same collector loop | After the idle check, add: `if (activation.HasExceededLifetime(now)) activation.Deactivate(DeactivationReason.MaxLifetimeExpired);`. Reuses the existing `Deactivate()` teardown. A silo with only TTL types (no idle age) must still start the collector — see Semantics. |
| Enable collector when only TTL is set | `GrainIdleCollector.ExecuteAsync` early-returns when `GrainCollectionAge == TimeSpan.Zero` (`GrainIdleCollector.cs:27`) | Change the guard to also run when any per-type `IdleCollectionAge` or `MaxActivationLifetime` is configured. `IGrainTypePolicyProvider` can expose `AnyLifecyclePoliciesConfigured` computed from options + registered types at startup. |
| Per-type concurrency limit (#123) | `GrainActivationTable.GetOrCreateAsync` (`GrainActivationTable.cs:61`) — the atomic add point that already enforces silo-wide `MaxActivations` at `GrainActivationTable.cs:71` | Add a `ConcurrentDictionary<string, int>` per-type counter. Accept an optional `int perTypeLimit` param; check the per-type count before add (same best-effort race note as the silo-wide check), increment on real add, decrement in `Remove` (`GrainActivationTable.cs:142`) / `RemoveIfFaulted` / `TryDeactivateAsync`. |
| New deactivation reason | `DeactivationReason` (`Grains/DeactivationReason.cs`) | Add `public static readonly DeactivationReason MaxLifetimeExpired = new("MaxLifetimeExpired");`. |
| New exception detail | `GrainActivationLimitExceededException` | Add a `GrainType? Type` + `bool PerType` (or a `GrainTypeActivationLimitExceededException` subclass) so callers can distinguish silo-wide vs per-type rejection. |
| DI wiring | `RuntimeServiceCollectionExtensions.cs:41` (where `IPlacementStrategyResolver` is `TryAddSingleton`) | `services.TryAddSingleton<IGrainTypePolicyProvider, AttributeGrainTypePolicyProvider>();` |

## Semantics

### TTL expiry with in-flight calls — **drain-then-deactivate (recommended)**
TTL reuses `GrainActivation.Deactivate()` (`GrainActivation.cs:242`), which already gives drain-then-deactivate for free:
1. `Deactivate()` flips status to `Deactivating` and **posts the teardown as the next mailbox work item** behind everything already queued.
2. The FIFO `Channel<MailboxWorkItem>` **drains all work items enqueued before the teardown** — every in-flight/queued call at expiry runs to completion.
3. The teardown item runs `OnDeactivateAsync`, disposes managed holders, then calls `_queue.Writer.TryComplete()`.
4. Work items enqueued **after** the teardown was posted (i.e. calls that arrived after TTL fired) are rejected by the completed writer — the caller's invoker path re-activates a fresh instance transparently on the next call. This is identical to the existing idle-collection behavior; TTL introduces no new mailbox semantics.

No hard mid-call cutoff — that is a deliberate rejection of the "hard cutoff" alternative in #116, which would fault in-flight calls for no benefit.

### Per-type limit hit — **reject with typed exception (recommended), not queue-wait**
When the per-type activation count is at `MaxActivationsPerSilo`, a request to activate a *new* grain of that type throws `GrainActivationLimitExceededException` (per-type flavor). Calls to *already-active* grains of that type are unaffected — same rule as the existing silo-wide cap.

Rejected over queue-wait because:
- It is **consistent** with the existing `MaxActivations` behavior (reject-fast), so no behavioral surprise.
- Queue-wait needs an activation-admission waiter/backpressure primitive that does not exist on this path and would let overload manifest as unbounded latency instead of a clear, retryable, observable signal.
- The caller/gateway can retry, shed, or redirect — a typed exception carrying `GrainType` + `Limit` gives them what they need. (A future opt-in `AdmissionMode.Wait` can be layered on without changing the default.)

### TTL interaction with `DelayDeactivation` — **TTL ignores the delay deadline**
- **Idle collection (both silo-global and per-type age)** continues to honor `DelayDeactivation` via the existing `IsDeactivationAllowed(now)` gate (`GrainActivation.cs:287`) — unchanged.
- **TTL does NOT consult `IsDeactivationAllowed`.** Rationale: #116's entire premise is bounding lifetime *even under continuous traffic*; letting `DelayDeactivation` postpone TTL would defeat the feature (a grain calling `DelayDeactivation` every call would live forever). Correctness is still preserved because drain-then-deactivate never interrupts an in-flight call — the delay hint is about *scheduling* deactivation, and TTL's drain already provides the safety the hint exists to guarantee. Documented explicitly: **`DelayDeactivation` defers idle collection but not TTL.**

### Per-type vs silo-wide limit interaction
Both checks apply; the **more restrictive wins**. A new activation must pass both the silo-wide `MaxActivations` gate and the per-type `MaxActivationsPerSilo` gate. Silo-wide check runs first (cheapest, protects total memory), then per-type.

## AOT notes
- **Resolution mechanism is identical to `AttributePlacementStrategyResolver`**: `Attribute.IsDefined` / `GetCustomAttribute` over a **concrete `Type` already registered in `GrainTypeRegistry`** and rooted by explicit DI registration. No `Assembly` scanning, no `Type.GetType(string)`, no reflection over unrooted types → trim-safe, and does not trip QRK0001/QRK0002/QRK0003.
- Custom attributes on a rooted, explicitly-registered type are preserved by the trimmer (the type is a registration root). Same trust boundary placement already relies on.
- Resolution is **cached per `Type`** and additionally **materialized onto the `GrainActivation`** at activation time, so the steady-state hot paths (mailbox scan in the collector, per-call invoke) do zero reflection.
- **Generator support is optional, not required.** The attribute-reflection path works standalone. As a follow-up, `BehaviorRegistrationGenerator` may emit each type's resolved `GrainTypePolicy` into `QuarkRegistrations.g.cs` (e.g. `services.AddGrainTypePolicy(new GrainType("Session"), new GrainTypePolicy { … })`) to make the resolver fully reflection-free for consumers who set `EnableTrimAnalyzer` strict — recommended but sequenced after the reflection path lands.

## Test plan
- **Unit — resolver precedence:** attribute-only, options-only, options-overrides-attribute-per-field, all-unset → all-null (inherits). Cache returns same instance per `Type`.
- **Unit — per-type idle (#115):** two types, different `IdleCollectionAge`; advance clock; assert only the shorter-age type is collected. Verify silo-global default still applies to a type with no policy.
- **Unit — TTL (#116):** a grain under continuous (never-idle) calls with a short `MaxActivationLifetime`; assert it deactivates after TTL; assert an in-flight call at expiry **completes** (drain) and a post-expiry call re-activates a fresh instance. Assert `DelayDeactivation` does **not** postpone TTL but **does** postpone idle collection.
- **Unit — per-type limit (#123):** fill `MaxActivationsPerSilo` for type A; assert new type-A activation throws the typed exception while type B still activates freely; assert calls to existing type-A grains still succeed; assert count decrements on deactivation and a slot frees up. Concurrency stress: N parallel activators, count never exceeds limit by more than in-flight-creators (mirror existing best-effort race note).
- **Unit — collector enablement:** silo with `GrainCollectionAge == Zero` but a type declaring only TTL → collector still runs.
- **Integration (`Quark.Tests.Integration` via `TestCluster`):** end-to-end grain with `[GrainTypePolicy]` attribute honored through real activation/collection; options-override path through `ConfigureSiloServices`.
- **Fault (`Quark.Tests.Fault`):** TTL deactivation racing with an in-flight failing call; per-type-limit rejection surfaced cleanly through the invoker.
- **AOT smoke:** `dotnet publish … /p:PublishAot=true` of a silo using the attribute — no new trim warnings.

## Implementation checklist
- [ ] `GrainTypePolicy` record — `Quark.Core.Abstractions/Grains/GrainTypePolicy.cs`.
- [ ] `GrainTypePolicyAttribute` — `Quark.Core.Abstractions/Grains/GrainTypePolicyAttribute.cs`.
- [ ] `DeactivationReason.MaxLifetimeExpired` — extend `Quark.Core.Abstractions/Grains/DeactivationReason.cs`.
- [ ] `IGrainTypePolicyProvider` interface — `Quark.Runtime/IGrainTypePolicyProvider.cs`.
- [ ] `AttributeGrainTypePolicyProvider` (attribute cache + options merge) — `Quark.Runtime/AttributeGrainTypePolicyProvider.cs` (model on `AttributePlacementStrategyResolver`).
- [ ] `SiloRuntimeOptions.GrainTypePolicies` dictionary — extend `Quark.Runtime/SiloRuntimeOptions.cs`.
- [ ] `GrainActivation`: add effective `_idleCollectionAge`/`_maxLifetime` ctor params + fields; `HasExceededLifetime(now)`; per-type-age `IsIdleLongerThan` — `Quark.Runtime/GrainActivation.cs`.
- [ ] `GrainActivationTable`: per-type counter + `perTypeLimit` param on `GetOrCreateAsync`; increment/decrement on add/`Remove`/`RemoveIfFaulted`/`TryDeactivateAsync` — `Quark.Runtime/GrainActivationTable.cs`.
- [ ] Extend/subclass `GrainActivationLimitExceededException` for per-type rejection — `Quark.Runtime/GrainActivationLimitExceededException.cs`.
- [ ] `LocalGrainCallInvoker.CreateActivationAsync`: resolve policy beside the `ReentrantAttribute` read; pass idle age + TTL to ctor; pass per-type limit to `GetOrCreateAsync` — `Quark.Runtime/LocalGrainCallInvoker.cs` (~line 284).
- [ ] `GrainIdleCollector`: use per-activation effective idle age; add TTL check; broaden the `ExecuteAsync` enablement guard — `Quark.Runtime/GrainIdleCollector.cs`.
- [ ] DI: `TryAddSingleton<IGrainTypePolicyProvider, AttributeGrainTypePolicyProvider>()` — `Quark.Runtime/RuntimeServiceCollectionExtensions.cs` (~line 41); optional `AddGrainTypePolicy(...)` helper.
- [ ] Tests: unit (resolver, idle, TTL, limit, enablement), integration, fault — per Test plan.
- [ ] (Follow-up, optional) `BehaviorRegistrationGenerator` emits resolved policy for a reflection-free path — `Quark.CodeGenerator/BehaviorRegistrationGenerator.cs`.
- [ ] Docs: `wiki/Architecture.md` (lifecycle) + `FEATURES.md` parity tracker; note in `quark-host-setup` skill.

## Resolved design decisions

**Q (all): one surface or three knobs?** → **One `GrainTypePolicy` record**, three nullable fields, one attribute + one options dictionary. Rationale: #123 itself asks for this; the three asks share resolution, caching, precedence, and the activation anchor — three parallel mechanisms would triplicate the resolver/cache/DI with no benefit.

**Q (#115): attribute vs `GrainType`-keyed options?** → **Both, attribute as author-default, options as ops-override, options win per-field.** Rationale: matches the placement pattern (attribute + resolver override) and lets ops retune without a redeploy.

**Q (#115): how is the attribute read AOT-safely?** → **Exactly like `AttributePlacementStrategyResolver`** — reflect over the concrete `Type` from `GrainTypeRegistry`, cache per `Type`. No new AOT surface.

**Q (#116): new collector or extend `GrainIdleCollector`?** → **Extend `GrainIdleCollector`.** Rationale: it already iterates `GetActiveActivations()` each cycle with the clock in hand; adding a TTL predicate is a few lines and avoids a second timer/service. Broaden its start guard so a TTL-only silo still runs it.

**Q (#116): in-flight calls at TTL expiry — drain or hard cutoff?** → **Drain-then-deactivate.** Reuses `Deactivate()`'s existing behavior: queued/in-flight items drain, teardown runs last, post-expiry calls re-activate fresh. Hard cutoff rejected (faults in-flight work for no gain).

**Q (#116): does `DelayDeactivation` defer TTL?** → **No.** Idle collection honors `DelayDeactivation`; TTL does not (else continuous `DelayDeactivation` defeats TTL's purpose). Drain-then-deactivate already guarantees no mid-call interruption, which is the safety `DelayDeactivation` exists to provide.

**Q (#116): where is lifetime measured from?** → The existing `_activatedAtTicks` (Stopwatch), set in `MarkActive()`. Measures from *activation complete*, so activation cost isn't counted against the TTL.

**Q (#123): options dictionary vs attribute?** → **Both** (same answer as #115) — per-type limit is declared on the behavior and overridable by ops.

**Q (#123): limit-hit — reject or queue-wait?** → **Reject with a typed exception.** Consistent with the existing silo-wide `MaxActivations`; queue-wait needs an admission-backpressure primitive that doesn't exist and hides overload as latency. Future opt-in `AdmissionMode.Wait` can layer on.

**Q (#123): interaction with silo-wide `MaxActivations`?** → **Both apply; most restrictive wins.** Silo-wide checked first, then per-type.

**Q (#123): per-silo or cluster-wide?** → **Per-silo** (`MaxActivationsPerSilo`), matching existing scope. Cluster-wide is a non-goal (needs directory/placement coordination).

**Q (#123 / #109): include mailbox priority?** → **No — excluded.** It is a scheduling concern needing a priority mailbox, orthogonal to lifecycle/capacity; folding it in would couple unrelated subsystems. Separate design.

**Q (all): is a source generator required?** → **No.** The reflection path (mirroring placement) is sufficient and AOT-safe. A generator-emitted policy is an optional follow-up for a fully reflection-free registration, sequenced after the core lands.
