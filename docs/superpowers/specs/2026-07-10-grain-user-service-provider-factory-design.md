# Design: Opt-in user-service-provider factory, replacing the grain-scope-initializer family

**Issue:** #162
**Date:** 2026-07-10
**Status:** Draft — ready for implementation

---

## 1. Problem statement

A benchmark run against issue #162 surfaced DI overhead in the per-call activation path.
`GrainActivation.RunActivationAsync` (`src/Quark.Runtime/GrainActivation.cs:881-891`) creates a fresh
`IServiceScope` from the root `IServiceProvider` on **every single grain call**:

```csharp
internal async Task RunActivationAsync(CancellationToken ct)
{
    using IServiceScope scope = _root.CreateScope();
    IServiceProvider sp = scope.ServiceProvider;
    IGrainBehavior behavior = await GrainScopeBinder.BindAndResolveAsync(sp, this, ct).ConfigureAwait(false);
    await RunEagerInitAsync(sp, ct).ConfigureAwait(false);
    if (behavior is IActivationLifecycle lifecycle)
    {
        await lifecycle.OnActivateAsync(ct).ConfigureAwait(false);
    }
}
```

`GrainScopeBinder.BindAndResolveAsync` (`src/Quark.Runtime/GrainScopeBinder.cs:9-27`) then, inside that
fresh scope, binds the shell accessor, sets `ICallContext`, optionally runs a registered
`GrainScopeInitializer`, and resolves the behavior via `IBehaviorResolver.Resolve` —
`BehaviorResolver.Resolve` (`src/Quark.Runtime/BehaviorResolver.cs:11-28`) calls a **compile-time-generated
factory** (`Func<IServiceProvider, IGrainBehavior>` from `GrainBehaviorFactoryRegistry`,
`src/Quark.Runtime/GrainBehaviorFactoryRegistry.cs`) that does explicit
`new MyBehavior(sp.GetRequiredService<T>(), ...)` calls per constructor parameter — no reflection.

For grain types whose behavior constructors resolve non-trivial user services (e.g. a repository backed
by a connection pool, a rules engine, anything with real construction cost), re-resolving that whole
dependency graph on every call is pure waste when the service is effectively stateless/reusable across
calls. There is currently **no way to avoid this** short of making every dependency a singleton at
registration time — which doesn't help because the *scope creation and per-call resolution* is what's
being paid for, not the singleton/scoped distinction itself.

### Today's scope-initializer family (all removed by this spec)

- `GrainScopeInitializer` (delegate) — `src/Quark.Core.Abstractions/Hosting/GrainScopeInitializer.cs:7-10`
- `IGrainScopeInitializerRegistry` / `GrainScopeInitializerRegistry` —
  `src/Quark.Runtime/IGrainScopeInitializerRegistry.cs`, `src/Quark.Runtime/GrainScopeInitializerRegistry.cs`
- `AddGrainScopeInitializer<TInterface, TBehavior>()` —
  `src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs:220-237`
- `GrainScopeInitializerRegistration` (deferred marker) — `RuntimeServiceCollectionExtensions.cs:342-346`
- `SiloHostedService.ApplyScopeInitializerRegistrations()` — `src/Quark.Runtime/SiloHostedService.cs:157-168`

This family lets a developer run a callback **inside** the already-created fresh scope — it does nothing
to avoid the scope-creation cost itself, since the scope already exists by the time the initializer runs.
It solves a different problem (populate/mutate the scope after creation) than the one this spec addresses
(skip re-resolving an expensive user dependency graph every call). Confirmed sole usages in the repo:
`tests/Quark.Tests.Unit/Runtime/GrainScopeInitializerTests.cs` and one reference in
`AddGrainBehaviorFactoryOverloadTests.cs` — no samples depend on it.

### Conclusion

Introduce a single, centralized, opt-in extensibility point **on the behavior class itself** that lets a
developer control how *their own* services are resolved and cached across calls, while Quark's own
framework services (`ICallContext`, `IActivationShellAccessor`, `IBehaviorResolver`, the persistence
accessors: `IActivationMemory<T>`, `IPersistentActivationMemory<T>`, `IManagedActivationMemory<T>`,
`IPersistentState<T>`) remain exclusively engine-managed, never sourced from the developer's resolver.
Remove the scope-initializer family entirely — it is superseded, not extended.

---

## 2. Goals / Non-goals

### Goals
- One opt-in mechanism, declared on the behavior class, for a developer to supply their own
  long-lived provider for **their own** constructor-injected services — avoiding re-resolution of an
  expensive user dependency graph on every call.
- Behaviors that don't opt in are **completely unaffected** — identical fresh-scope-per-call behavior,
  zero change to registration wiring or runtime cost.
- A structural (not conventional) guarantee that Quark's own services are never resolved through the
  developer-supplied provider — the split is enforced by construction, not by developer discipline.
- AOT/trim-safe: no reflection, no runtime type scanning; resolved entirely via compile-time source
  generation, consistent with the rest of `Quark.CodeGenerator`.
- Remove `GrainScopeInitializer`/`IGrainScopeInitializerRegistry`/`AddGrainScopeInitializer` and their
  startup-application step — one centralized mechanism replaces a family of three coupled APIs.

### Non-goals
- **Not** eliminating per-call scope creation for Quark's own services. Quark's own per-call state is
  still built from a scope (a small one — see §4) every call; the saving targeted here is skipping
  re-resolution of the *user's* dependency graph, which is what the benchmark showed as the actual cost
  for grains with non-trivial user dependencies.
- **Not** a general per-activation (per grain-key) customization. The factory runs once per **grain type**
  at silo startup and the resulting provider is shared by every activation of that type — see §7 open
  question 1 for the tradeoff this accepts.
- **Not** changing anything about placement, activation lifecycle (`OnActivateAsync`/`OnDeactivateAsync`),
  or `RunEagerInitAsync` — these continue to run exactly as today, over whichever composite provider is in
  effect.
- **Not** touching non-generator-based (hand-wired) behavior registration paths used in test projects
  (`tests/Quark.Tests.Unit/Integration/`) — those keep constructing behaviors manually; this spec's
  mechanism is generator-driven only.
- **Not** supporting `IPersistentActivationMemory<T>` / `[PersistentState]` (`IPersistentState<T>`) /
  `ITransactionalState<T>` / streams / reminders on opted-in behaviors in this first cut. Those all need
  `IStorage<T>`/`IGrainStorage` (or other cross-package services) registered by separate packages
  (`Quark.Persistence.InMemory`, `Quark.Persistence.Redis`, etc.) via their own extension methods, which
  this spec does not touch. `IActivationMemory<T>`, `IManagedActivationMemory<T>`, and
  `IEagerActivationMemory<T>` **are** supported — they only need the activation shell, nothing
  external (§4.2). A behavior that opts into `IGrainUserServiceProviderFactory` and *also* takes one of
  the unsupported types fails fast at activation with a clear `InvalidOperationException` ("Unable to
  resolve service...") — not silently wrong. Extending storage/stream/reminder provider registration to
  flow into the satellite provider is a natural follow-up, out of scope here (confirmed decision, not an
  oversight).

---

## 3. Architecture overview

The satellite "Quark-only" provider is **not** built by teaching the generator to emit a second,
duplicated registration method. Instead, the four generator-emitted accessor calls
(`IActivationMemory<T>`, `IManagedActivationMemory<T>`, plus the `AddEagerActivationMemory<T>` helper)
switch from plain `services.AddScoped<T>(factory)` to a new `services.AddQuarkOwnedScoped<T>(factory)`
extension that *also* drops a deferred marker recording `factory`. At startup, replaying every captured
marker onto a fresh `IServiceCollection` reconstructs an equivalent Quark-only registration set with zero
duplicated emission logic — the same deferred-marker idiom already used for
`IGrainBehaviorRegistration`/`IGrainPlacementStrategyRegistration` elsewhere in this file.

```
compile time (per assembly):
  BehaviorRegistrationGenerator scans IGrainBehavior implementers (as today) AND additionally
  checks whether each implements IGrainUserServiceProviderFactory. If so, it emits a deferred
  IUserServiceProviderFactoryRegistration (GrainType → the static CreateUserServiceProvider call),
  the same idiom as the removed IGrainScopeInitializerRegistration.
  Separately (independent of opt-in), its IActivationMemory<T>/IManagedActivationMemory<T>/
  IEagerActivationMemory<T> accessor emissions now call AddQuarkOwnedScoped<T> instead of AddScoped<T> —
  same factory lambda, one wrapper method, so every assembly's accessors are marker-capturable.

silo startup (SiloHostedService.StartAsync, after grain/factory/placement registrations apply):
  - IUserServiceProviderRegistry is populated: for each deferred factory registration,
    call TBehavior.CreateUserServiceProvider(appRoot) ONCE and cache the result by GrainType.
  - IF any such registration exists, build the Quark-only satellite root:
      var quarkOnly = new ServiceCollection();
      quarkOnly.AddSingleton(mainTypeRegistry); quarkOnly.AddSingleton<IGrainTypeRegistry>(mainTypeRegistry);
      quarkOnly.AddSingleton(mainFactoryRegistry);                      // SAME instances as the main root
      quarkOnly.AddScoped<ActivationShellAccessor>(); ... AddScoped<IBehaviorResolver, BehaviorResolver>();
      foreach (var marker in _services.GetServices<IQuarkOwnedServiceRegistration>()) marker.Apply(quarkOnly);
      QuarkOnlyServiceProviderHolder.Provider = quarkOnly.BuildServiceProvider();
    (skipped entirely — zero cost — for silos with no opted-in behaviors)

per call, GrainActivation.RunActivationAsync:
  registry.TryGet(GrainType, out userProvider) && holder.Provider is { } quarkRoot ?
    NO  → today's path, unchanged: using scope = _root.CreateScope();
          GrainScopeBinder.BindAndResolveAsync(sp, sp, this, ct)  // same provider for binding + construction
    YES → using quarkScope = quarkRoot.CreateScope();               // small — Quark's own types only
          var composite = new CompositeServiceProvider(quarkScope.ServiceProvider, userProvider);  // quark-first
          GrainScopeBinder.BindAndResolveAsync(quarkScope.ServiceProvider, composite, this, ct)
          RunEagerInitAsync(composite, ct); OnActivateAsync as today.
```

`CompositeServiceProvider` tries the Quark-only side **first**, falling back to the cached user provider.
This ordering is load-bearing, not arbitrary: if a developer's `CreateUserServiceProvider` returns
`rootServices` unchanged (a natural, common choice — "my services are already cheap to resolve from the
app root"), that root also contains Quark's own type registrations (same flat `silo.Services` collection).
Querying it first would silently resolve `ICallContext`/etc. as a captive, cross-call-shared instance
instead of Quark's real per-call one — a correctness bug, not just a missed optimization. Quark-first
resolution makes the "only user services, never Quark services" guarantee structural regardless of what
the developer's provider happens to also contain.

**`IBehaviorResolver` changes shape to make this safe.** Today `BehaviorResolver` captures `IServiceProvider
scope` in its own constructor (`BehaviorResolver.cs:6-9`) and uses that captured instance to construct the
behavior — but when `BehaviorResolver` itself is resolved from the Quark-only scope, MS.DI would inject
*that scope's own* provider as `scope`, not the outer composite, silently starving the behavior's
user-owned constructor parameters. The fix: `IBehaviorResolver.Resolve` takes the construction provider as
an explicit parameter instead of relying on ambient constructor capture —
`IGrainBehavior Resolve(GrainType grainType, IServiceProvider services)` — so the caller always controls
which provider builds the behavior, decoupled from which provider resolved `IBehaviorResolver` itself.

---

## 4. New API surface

### 4.1 The factory interface (new, `Quark.Core.Abstractions`)

```csharp
namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Opt-in, compile-time-discovered factory that supplies the IServiceProvider used to resolve a
///     behavior's OWN (non-Quark) constructor-injected services. Implemented directly on the behavior
///     class. Called once per grain type at silo startup; the returned provider is cached and shared by
///     every activation of that type for the process lifetime — see §7 open question 1.
/// </summary>
public interface IGrainUserServiceProviderFactory
{
    /// <param name="rootServices">
    ///     The ordinary root IServiceProvider built from the silo's registered services (silo.Services).
    ///     Use this to pull already-registered user singletons, or return it unchanged if the developer's
    ///     services are already cheap/stateless to resolve from it directly.
    /// </param>
    static abstract IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices);
}
```

A behavior opts in by implementing this interface directly:

```csharp
public sealed class MyBehavior(ICallContext ctx, IMyExpensiveRepo repo) : IMyGrain, IGrainUserServiceProviderFactory
{
    public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices) => rootServices;
    // ...
}
```

Static interface members require real C# polymorphism (the concrete type is known at the generic/
compile-time call site) — no reflection is involved in dispatching to it.

### 4.2 Generator wiring (`Quark.CodeGenerator`)

`BehaviorRegistrationGenerator` already discovers every `IGrainBehavior` implementer per assembly at
compile time (`BehaviorRegistrationGenerator.cs`). It is extended to:

1. Detect whether the concrete behavior type also implements `IGrainUserServiceProviderFactory`.
2. If so, emit a deferred `IUserServiceProviderFactoryRegistration` (same idiom as the removed
   `IGrainScopeInitializerRegistration`) calling `TBehavior.CreateUserServiceProvider` directly — a plain
   static call, not generic dispatch, since the concrete type is known at compile time.
3. Change its **existing** `IActivationMemory<T>`/`IManagedActivationMemory<T>` inline emissions
   (`BehaviorRegistrationGenerator.cs:443-447, 464-468`) and the `AddEagerActivationMemory<T>` helper body
   (`RuntimeServiceCollectionExtensions.cs:278-287`) from `services.AddScoped<T>(factory)` to
   `services.AddQuarkOwnedScoped<T>(factory)` — same factory lambda, new wrapper — so every assembly's
   accessor registrations become replayable onto the satellite collection (§3). This applies to *every*
   behavior's accessors, not just opted-in ones — harmless, since nothing consumes the marker unless at
   least one behavior in the process opts in. `IPersistentActivationMemory<T>`/`[PersistentState]` inline
   emissions are intentionally **not** changed (§2 non-goal).

### 4.3 Startup application (`Quark.Runtime`)

`SiloHostedService.ApplyScopeInitializerRegistrations()` is replaced by `ApplyUserServiceProviderFactoryRegistrations()`,
which:
1. Populates the new `IUserServiceProviderRegistry` (`ConcurrentDictionary<GrainType, IServiceProvider>` —
   same shape as the removed `GrainScopeInitializerRegistry`) by invoking each deferred factory once
   against the app root `IServiceProvider`.
2. If at least one such registration exists, builds the Quark-only satellite root exactly as described in
   §3 (fresh `ServiceCollection`, the 6 fixed core lines, the main root's *existing*
   `GrainTypeRegistry`/`GrainBehaviorFactoryRegistry` **instances** registered by reference — not
   rebuilt — plus every captured `IQuarkOwnedServiceRegistration` marker replayed onto it), assigning the
   result to a mutable `QuarkOnlyServiceProviderHolder` singleton (registered `null` by default in
   `AddQuarkRuntime()`) so `GrainActivation` can read it without any constructor signature change. The
   satellite provider is disposed in `SiloHostedService.StopAsync` alongside existing teardown.

### 4.4 Runtime call flow (`Quark.Runtime`)

`GrainActivation.RunActivationAsync` branches on `_root.GetRequiredService<IUserServiceProviderRegistry>()
.TryGet(GrainType, ...)` combined with `_root.GetRequiredService<QuarkOnlyServiceProviderHolder>().Provider`
being non-null. When both hold: create the **Quark-only** scope (`quarkRoot.CreateScope()`) instead of the
flat `_root.CreateScope()`, bind `ICallContext`/shell accessor into it exactly as today (via
`GrainScopeBinder.BindAndResolveAsync`, now taking separate `bindingServices`/`constructionServices`
parameters — see §3), and pass the small `CompositeServiceProvider` (Quark-only first, cached user provider
second) as the construction provider to `IBehaviorResolver.Resolve(grainType, constructionServices)`.
Behaviors that don't implement the interface take the existing, entirely unchanged path — same provider
passed for both binding and construction, identical to today's single-`sp` flow.

---

## 5. Failure & edge cases

| Case | Behaviour |
|---|---|
| `CreateUserServiceProvider` throws at startup | Silo startup fails fast (same failure mode as any other misconfigured DI registration today) — surfaced before any activation is attempted, not deferred to first call. |
| Behavior implements `IGrainUserServiceProviderFactory` but has no non-Quark constructor dependencies | No-op in effect: the cached provider is simply never consulted, since every parameter resolves from the Quark-only scope. Harmless, not an error. |
| A constructor parameter type is ambiguous (registered in both the Quark-only satellite AND the cached user provider) | Quark-only scope wins (queried first in `CompositeServiceProvider`) — this can only happen if a developer manually registers a Quark abstraction type into their own `rootServices`/user provider, which is a misuse; document it as undefined/discouraged rather than guarding it at runtime. |
| Grain type never activated | Factory still runs once at startup (eager, not lazy) — consistent with "known cost paid once, upfront" rather than adding a first-call branch to check. |

---

## 6. AOT / trim safety

- **No reflection.** Static interface member dispatch is resolved by the generic/compile-time call site
  the generator emits — the same guarantee `BehaviorRegistrationGenerator` already provides for
  `IGrainBehavior` discovery.
- **No assembly scanning** — purely additive to the existing per-assembly generator pass.
- **`CompositeServiceProvider`** is a small hand-written class with two `IServiceProvider` fields and a
  `GetService(Type)` that tries one then the other — no dynamic codegen, no `Type`-keyed reflection beyond
  what `IServiceProvider.GetService` already does.
- **AOT smoke:** extend the existing `PublishAot=true` runtime smoke build with a behavior implementing
  `IGrainUserServiceProviderFactory` — must stay warning-free.

---

## 7. Open questions

1. **Per-grain-type sharing, not per-activation.** The cached user provider is shared by every activation
   of a grain type, not scoped per grain key. This is a deliberate simplification (confirmed during
   design): a developer who needs per-key variance in their user services should encode that inside their
   own provider (e.g. keyed internally), not rely on Quark to create one provider per activation. Flag if
   a real use case needs per-activation granularity — it would require a different mechanism (see the
   rejected "lazy cache-on-first-call" alternative considered during design).
2. **Eager vs. lazy factory invocation.** Chosen: eager, at startup, for every registered grain type
   regardless of whether it's ever activated. Alternative: lazy on first activation. Eager was chosen for
   fail-fast startup validation; revisit if silos register many grain types that are rarely activated and
   startup cost becomes material.
3. **Persistence-pattern support deferred (§2 non-goal, confirmed during design).** `IPersistentActivationMemory<T>`,
   `[PersistentState]`, `ITransactionalState<T>`, streams, and reminders are not resolvable by opted-in
   behaviors in v1 — they need `IStorage<T>`/`IGrainStorage` and other services from packages this spec
   doesn't touch. Extending `Quark.Persistence.InMemory`/`Redis` (and similar) to register through
   `AddQuarkOwnedScoped` is the natural follow-up once this ships.

---

## Known limitations (v1)

- **Persistence patterns are unsupported on opted-in behaviors.** `IPersistentActivationMemory<T>`,
  `[PersistentState]` (`IPersistentState<T>`), `ITransactionalState<T>`, streams, and reminders cannot be
  combined with `IGrainUserServiceProviderFactory` in this first cut (§2 non-goal, §7 open question 3) —
  they need `IStorage<T>`/`IGrainStorage` and other cross-package services this spec does not touch.
  `BehaviorRegistrationGenerator` now reports this combination as a compile-time error (`QRK0056`) instead
  of letting it fail at runtime with a confusing exception.
- **`CompositeServiceProvider` does not merge `IEnumerable<T>` registrations across primary/secondary.**
  MS.DI always returns a non-null (possibly empty) collection for `IEnumerable<T>`, so the Quark-only
  primary side's (possibly empty) result always wins and the cached user provider's registrations for that
  type are never consulted — the `??` fallback never triggers. A correct generic merge would require
  reflection-based array construction (`Array.CreateInstance`/`MakeGenericMethod`), which conflicts with
  this codebase's AOT-safety mandate. If a behavior needs multiple registered implementations of an
  interface, aggregate them inside `CreateUserServiceProvider` itself rather than relying on cross-boundary
  `IEnumerable<T>` resolution.

---

## 8. Implementation sequence

1. `Quark.Core.Abstractions/Hosting/IGrainUserServiceProviderFactory.cs` — new interface (§4.1).
2. `Quark.Runtime/CompositeServiceProvider.cs` — the two-provider fallback `IServiceProvider` (§4.4).
3. `Quark.Runtime/IUserServiceProviderRegistry.cs` + implementation — replaces
   `IGrainScopeInitializerRegistry`/`GrainScopeInitializerRegistry`.
4. `Quark.CodeGenerator/BehaviorRegistrationGenerator.cs` — detect `IGrainUserServiceProviderFactory`
   implementers; emit the deferred factory-registration call and the Quark-only satellite collection
   entries (§4.2).
5. `Quark.Runtime/SiloHostedService.cs` — replace `ApplyScopeInitializerRegistrations()` with the
   eager factory-invocation step that builds `IUserServiceProviderRegistry` and (if needed) the
   Quark-only satellite root (§4.3).
6. `Quark.Runtime/GrainActivation.cs` + `Quark.Runtime/GrainScopeBinder.cs` — branch on
   `IUserServiceProviderRegistry.TryGet` (§4.4).
7. Remove `GrainScopeInitializer`, `IGrainScopeInitializerRegistry`, `GrainScopeInitializerRegistry`,
   `AddGrainScopeInitializer<TInterface,TBehavior>()`, `GrainScopeInitializerRegistration`, and
   `SiloHostedService.ApplyScopeInitializerRegistrations()`.
8. Update/replace `tests/Quark.Tests.Unit/Runtime/GrainScopeInitializerTests.cs` and the reference in
   `AddGrainBehaviorFactoryOverloadTests.cs` with coverage for the new mechanism (opted-in behavior reuses
   the cached user provider across calls; non-opted-in behavior is unaffected; Quark service types are
   never resolved from the cached user provider even if present there).
9. AOT smoke test per §6; update `FEATURES.md`, `wiki/Source-Generators.md`.

---

## 9. Testing strategy

- **Unit — opted-in behavior reuses the cached provider:** register a behavior whose
  `CreateUserServiceProvider` returns a provider wrapping a counting factory; drive two calls against the
  same activation; assert the user factory ran once, not twice.
- **Unit — non-opted-in behavior unaffected:** existing scope-per-call tests continue to pass unchanged.
- **Unit — Quark service resolution is structural, not conventional:** register a behavior whose
  `CreateUserServiceProvider` deliberately returns a provider that ALSO has an `ICallContext` registered
  (misuse) — assert the engine's own `ICallContext` instance is what the behavior actually receives, not
  the one from the user provider.
- **Unit — startup fail-fast:** a `CreateUserServiceProvider` that throws fails silo startup, not the
  first grain call.
- **Unit — activation lifecycle unaffected:** `OnActivateAsync`/`RunEagerInitAsync` run identically over
  the composite provider for opted-in behaviors.
- **AOT smoke** per §6.
