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

---

## 3. Architecture overview

```
compile time (per assembly):
  BehaviorRegistrationGenerator scans IGrainBehavior implementers (as today) AND additionally
  checks whether each implements IGrainUserServiceProviderFactory.

  For assemblies with at least one opted-in behavior, the generator ALSO emits a small satellite
  IServiceCollection containing only Quark's own registrations for those behaviors — the same
  per-behavior accessor registrations (IActivationMemory<T>, IPersistentActivationMemory<T>,
  IManagedActivationMemory<T>, IPersistentState<T>) it already emits into services, plus the fixed
  core (ICallContext, IActivationShellAccessor, ICallContextSetter, IBehaviorResolver) — into a
  SEPARATE collection, built into its own root provider ("the Quark root") at startup.

silo startup:
  SiloHostedService builds:
    - the ordinary root IServiceProvider from silo.Services (unchanged — "the user/app root")
    - (only if any behavior opted in) the Quark-only satellite root from the generated collection
  For each grain type registered via IGrainUserServiceProviderFactory:
    userProvider = TBehavior.CreateUserServiceProvider(appRoot)      // called ONCE, cached
    cache[grainType] = userProvider

per call, GrainActivation.RunActivationAsync:
  cache.TryGet(activation.GrainType, out userProvider)?
    NO  → today's path, unchanged: using scope = _root.CreateScope(); resolve via GrainScopeBinder as today.
    YES → using quarkScope = _quarkRoot.CreateScope();               // small, cheap — Quark's own types only
          sp = new CompositeServiceProvider(quarkScope.ServiceProvider, userProvider);
          bind ICallContext / shell accessor into quarkScope.ServiceProvider (as today, just narrower scope)
          behavior = factoryRegistry-generated factory(sp)           // unchanged factory, unchanged codegen
          RunEagerInitAsync(sp, ct); OnActivateAsync as today.
```

The generated `Func<IServiceProvider, IGrainBehavior>` factory (`BehaviorResolver.cs:11-28`,
`GrainBehaviorFactoryRegistry`) is **unchanged** in both branches — it always resolves constructor
parameters via `sp.GetRequiredService<T>()`. What changes is only which `IServiceProvider` is handed to
it: the full flat scope (default path) or the composite of a small Quark-only scope + the cached user
provider (opted-in path).

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
2. If so, emit a call to `TBehavior.CreateUserServiceProvider` into the generated
   `QuarkRegistrations.g.cs`, registered against that grain's `GrainType`, into a new deferred-marker
   registration list (replacing the removed `IGrainScopeInitializerRegistration` mechanism).
3. Emit the Quark-only satellite `IServiceCollection` entries (the fixed core + the same per-behavior
   accessor registrations it already emits — `IActivationMemory<T>` etc., `BehaviorRegistrationGenerator.cs:443-490`)
   for opted-in behaviors, into a **separate** collection rather than the shared one — only when at
   least one behavior in the assembly opts in.

### 4.3 Startup application (`Quark.Runtime`)

`SiloHostedService.ApplyScopeInitializerRegistrations()` is replaced by an equivalent step that, for each
deferred `IGrainUserServiceProviderFactory` registration, calls the generated static factory once against
the app root `IServiceProvider` and stores the result in a new `IUserServiceProviderRegistry`
(`ConcurrentDictionary<GrainType, IServiceProvider>` — same shape as the removed
`GrainScopeInitializerRegistry`, purpose-renamed). If the assembly emitted a Quark-only satellite
collection, it is built into its own root `IServiceProvider` at the same point.

### 4.4 Runtime call flow (`Quark.Runtime`)

`GrainActivation.RunActivationAsync` and `GrainScopeBinder.BindAndResolveAsync` gain a branch: if
`IUserServiceProviderRegistry.TryGet(activation.GrainType, ...)` finds a cached provider, create the
**Quark-only** scope (`_quarkRoot.CreateScope()`) instead of the flat `_root.CreateScope()`, bind
`ICallContext`/shell accessor into it exactly as today, and wrap it with the cached user provider in a
small internal `CompositeServiceProvider : IServiceProvider` (tries the Quark scope first, falls back to
the cached user provider) before calling `IBehaviorResolver.Resolve`. Behaviors that don't implement the
interface take the existing, entirely unchanged path.

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
3. **Satellite-collection duplication.** The Quark-only satellite collection duplicates registration code
   already emitted into the shared collection (same accessor registrations, different destination
   collection) for opted-in behaviors. Acceptable duplication given it's generator-emitted, not
   hand-maintained, but flag if a future refactor wants a single-source-of-truth registration list that
   fans out to both collections.

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
