# Grain User-Service-Provider Factory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `GrainScopeInitializer`/`IGrainScopeInitializerRegistry`/`AddGrainScopeInitializer` family with a single opt-in, compile-time-discovered `IGrainUserServiceProviderFactory` that lets a behavior class supply a cached, per-grain-type provider for its own (non-Quark) constructor-injected services — avoiding re-resolution of an expensive user dependency graph on every grain call, while Quark's own services remain exclusively engine-managed.

**Architecture:** A behavior opts in by implementing `static abstract IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)`. The source generator detects this and emits a deferred registration; at silo startup the factory runs once per grain type and the result is cached in `IUserServiceProviderRegistry`. A small "Quark-only" satellite `IServiceProvider` is built once (from the generator's own registrations, captured via a new `AddQuarkOwnedScoped` marker mechanism) so `GrainActivation.RunActivationAsync` can, for opted-in grain types, create a cheap Quark-only scope each call and compose it with the cached user provider via `CompositeServiceProvider` (Quark-first, structurally guaranteeing Quark services are never satisfied by the user's provider) instead of creating a full fresh scope from the flat root every call.

**Tech Stack:** .NET 10, `Microsoft.Extensions.DependencyInjection`, Roslyn incremental source generators (`Quark.CodeGenerator`), xUnit.

## Global Constraints

- Every production package has `IsTrimmable=true` / `EnableAotAnalyzer=true` — no reflection, no assembly scanning; all new registration/detection logic must be either plain code or generator-emitted.
- Never add `Version=` attributes to `<PackageReference>` — package versions are centrally managed in `Directory.Packages.props`.
- Follow existing house style: `internal` for engine-only types, `public` only for the extension methods/interfaces developers or generated code must call.
- v1 scope excludes `IPersistentActivationMemory<T>` / `[PersistentState]` / `ITransactionalState<T>` / streams / reminders for opted-in behaviors — those need cross-package services (`IStorage<T>` etc.) not covered here (confirmed, see spec §2).
- Spec: `docs/superpowers/specs/2026-07-10-grain-user-service-provider-factory-design.md` — read it for full rationale before starting.

---

## File Structure

New files:
- `src/Quark.Core.Abstractions/Hosting/IGrainUserServiceProviderFactory.cs` — the opt-in interface.
- `src/Quark.Runtime/CompositeServiceProvider.cs` — two-provider fallback `IServiceProvider`.
- `src/Quark.Runtime/IUserServiceProviderRegistry.cs` — registry interface (replaces `IGrainScopeInitializerRegistry`).
- `src/Quark.Runtime/UserServiceProviderRegistry.cs` — implementation.
- `src/Quark.Runtime/QuarkOnlyServiceProviderHolder.cs` — mutable holder for the lazily-built satellite provider.
- `tests/Quark.Tests.Unit/Runtime/CompositeServiceProviderTests.cs`
- `tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs` — replaces `GrainScopeInitializerTests.cs`.

Deleted files:
- `src/Quark.Core.Abstractions/Hosting/GrainScopeInitializer.cs`
- `src/Quark.Runtime/IGrainScopeInitializerRegistry.cs`
- `src/Quark.Runtime/GrainScopeInitializerRegistry.cs`
- `tests/Quark.Tests.Unit/Runtime/GrainScopeInitializerTests.cs`

Modified files:
- `src/Quark.Runtime/BehaviorResolver.cs`, `src/Quark.Runtime/IBehaviorResolver.cs` — `Resolve` takes an explicit construction provider.
- `src/Quark.Runtime/GrainScopeBinder.cs` — split binding/construction providers, drop unused async.
- `src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs` — new `AddQuarkOwnedScoped<T>`, new markers, new `AddGrainUserServiceProviderFactory<TInterface,TBehavior>`; remove `AddGrainScopeInitializer` family; wire new registry/holder into `AddQuarkRuntime()`; update `AddEagerActivationMemory<T>`.
- `src/Quark.Runtime/SiloHostedService.cs` — replace `ApplyScopeInitializerRegistrations` with `ApplyUserServiceProviderFactoryRegistrations`; dispose the satellite provider on stop.
- `src/Quark.Runtime/BehaviorStartupValidator.cs` — skip opted-in behaviors (their real construction path isn't ready yet at this point in hosted-service ordering).
- `src/Quark.Runtime/GrainActivation.cs` — `RunActivationAsync` branches on the registry + holder.
- `src/Quark.CodeGenerator/BehaviorRegistrationGenerator.cs` — detect the new interface, emit registration; switch `IActivationMemory<T>`/`IManagedActivationMemory<T>` inline emissions to `AddQuarkOwnedScoped`.
- `tests/Quark.Tests.Unit/Runtime/BehaviorResolverTests.cs` — update call sites for the new signature.
- `tests/Quark.Tests.Unit/Runtime/AddGrainBehaviorFactoryOverloadTests.cs` — replace the scope-initializer test with a user-service-provider-factory equivalent.
- `tests/Quark.Tests.CodeGenerator/BehaviorRegistrationGeneratorTests.cs` — new tests for the generator changes.
- `FEATURES.md`, `wiki/Source-Generators.md` — documentation.

---

## Task 1: `IGrainUserServiceProviderFactory` interface

**Files:**
- Create: `src/Quark.Core.Abstractions/Hosting/IGrainUserServiceProviderFactory.cs`
- Delete: `src/Quark.Core.Abstractions/Hosting/GrainScopeInitializer.cs`
- Test: `tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs` (created empty in this task, populated in Task 8)

**Interfaces:**
- Produces: `Quark.Core.Abstractions.Hosting.IGrainUserServiceProviderFactory` with `static abstract IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)` — every later task that references the opt-in mechanism uses this exact interface and method name.

- [ ] **Step 1: Delete the old delegate type**

Delete `src/Quark.Core.Abstractions/Hosting/GrainScopeInitializer.cs` entirely (its only consumers are removed in Tasks 4–6).

- [ ] **Step 2: Create the new interface**

Create `src/Quark.Core.Abstractions/Hosting/IGrainUserServiceProviderFactory.cs`:

```csharp
namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Opt-in, compile-time-discovered factory that supplies the IServiceProvider used to resolve a
///     behavior's own (non-Quark) constructor-injected services. Implemented directly on the behavior
///     class. Called once per grain type at silo startup; the returned provider is cached and shared by
///     every activation of that type for the process lifetime.
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

- [ ] **Step 3: Create an empty placeholder test file**

Create `tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs`:

```csharp
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class UserServiceProviderFactoryTests
{
    // Populated in Task 8, once GrainActivation/SiloHostedService/RuntimeServiceCollectionExtensions
    // wiring exists to actually exercise the opt-in flow end-to-end.
    [Fact]
    public void Placeholder() { }
}
```

- [ ] **Step 4: Build to confirm the old delegate's removal doesn't break anything yet**

Run: `dotnet build Quark.slnx`
Expected: FAILS — `GrainScopeInitializerRegistry.cs`, `IGrainScopeInitializerRegistry.cs`, `RuntimeServiceCollectionExtensions.cs`, `GrainScopeBinder.cs`, `GrainScopeInitializerTests.cs`, `AddGrainBehaviorFactoryOverloadTests.cs` still reference the deleted `GrainScopeInitializer` type. This is expected — those are cleaned up in Tasks 3–6 and 8. Confirm the ONLY errors are "type or namespace 'GrainScopeInitializer' could not be found" (or equivalent) in those specific files — no unrelated breakage.

- [ ] **Step 5: Commit**

```bash
git add src/Quark.Core.Abstractions/Hosting/IGrainUserServiceProviderFactory.cs \
        src/Quark.Core.Abstractions/Hosting/GrainScopeInitializer.cs \
        tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs
git commit -m "Add IGrainUserServiceProviderFactory, remove GrainScopeInitializer delegate"
```

---

## Task 2: `CompositeServiceProvider`

**Files:**
- Create: `src/Quark.Runtime/CompositeServiceProvider.cs`
- Test: `tests/Quark.Tests.Unit/Runtime/CompositeServiceProviderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Quark.Runtime.CompositeServiceProvider` — `internal sealed class CompositeServiceProvider(IServiceProvider primary, IServiceProvider secondary) : IServiceProvider` with `GetService(Type)` trying `primary` first, then `secondary`. Task 7 constructs this directly.

- [ ] **Step 1: Write the failing tests**

Create `tests/Quark.Tests.Unit/Runtime/CompositeServiceProviderTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class CompositeServiceProviderTests
{
    private interface IMarkerA;
    private interface IMarkerB;

    private sealed class MarkerA : IMarkerA;
    private sealed class MarkerB : IMarkerB;

    [Fact]
    public void GetService_ReturnsFromPrimary_WhenPrimaryHasIt()
    {
        var primaryServices = new ServiceCollection();
        primaryServices.AddSingleton<IMarkerA, MarkerA>();
        using ServiceProvider primary = primaryServices.BuildServiceProvider();

        var secondaryServices = new ServiceCollection();
        using ServiceProvider secondary = secondaryServices.BuildServiceProvider();

        var composite = new CompositeServiceProvider(primary, secondary);

        Assert.IsType<MarkerA>(composite.GetService(typeof(IMarkerA)));
    }

    [Fact]
    public void GetService_FallsBackToSecondary_WhenPrimaryDoesNotHaveIt()
    {
        var primaryServices = new ServiceCollection();
        using ServiceProvider primary = primaryServices.BuildServiceProvider();

        var secondaryServices = new ServiceCollection();
        secondaryServices.AddSingleton<IMarkerB, MarkerB>();
        using ServiceProvider secondary = secondaryServices.BuildServiceProvider();

        var composite = new CompositeServiceProvider(primary, secondary);

        Assert.IsType<MarkerB>(composite.GetService(typeof(IMarkerB)));
    }

    [Fact]
    public void GetService_ReturnsNull_WhenNeitherHasIt()
    {
        var primaryServices = new ServiceCollection();
        using ServiceProvider primary = primaryServices.BuildServiceProvider();

        var secondaryServices = new ServiceCollection();
        using ServiceProvider secondary = secondaryServices.BuildServiceProvider();

        var composite = new CompositeServiceProvider(primary, secondary);

        Assert.Null(composite.GetService(typeof(IMarkerA)));
    }

    [Fact]
    public void GetService_PrimaryWins_WhenBothHaveIt()
    {
        var primaryServices = new ServiceCollection();
        primaryServices.AddSingleton<IMarkerA, MarkerA>();
        using ServiceProvider primary = primaryServices.BuildServiceProvider();

        var secondaryServices = new ServiceCollection();
        var secondaryMarker = new MarkerA();
        secondaryServices.AddSingleton<IMarkerA>(secondaryMarker);
        using ServiceProvider secondary = secondaryServices.BuildServiceProvider();

        var composite = new CompositeServiceProvider(primary, secondary);

        Assert.NotSame(secondaryMarker, composite.GetService(typeof(IMarkerA)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~CompositeServiceProviderTests"`
Expected: FAIL to compile — `CompositeServiceProvider` doesn't exist yet.

- [ ] **Step 3: Implement**

Create `src/Quark.Runtime/CompositeServiceProvider.cs`:

```csharp
namespace Quark.Runtime;

/// <summary>
///     IServiceProvider that resolves from a primary provider first, falling back to a secondary
///     provider when the primary has no registration for the requested type. Used to compose Quark's
///     own per-call scope with a developer-supplied, cached user-service provider
///     (see IGrainUserServiceProviderFactory) without letting the user provider ever satisfy a
///     Quark-owned service type.
/// </summary>
internal sealed class CompositeServiceProvider(IServiceProvider primary, IServiceProvider secondary) : IServiceProvider
{
    public object? GetService(Type serviceType) => primary.GetService(serviceType) ?? secondary.GetService(serviceType);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~CompositeServiceProviderTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Quark.Runtime/CompositeServiceProvider.cs tests/Quark.Tests.Unit/Runtime/CompositeServiceProviderTests.cs
git commit -m "Add CompositeServiceProvider for the Quark-first fallback resolution"
```

---

## Task 3: `IUserServiceProviderRegistry` + `QuarkOnlyServiceProviderHolder`

**Files:**
- Create: `src/Quark.Runtime/IUserServiceProviderRegistry.cs`
- Create: `src/Quark.Runtime/UserServiceProviderRegistry.cs`
- Create: `src/Quark.Runtime/QuarkOnlyServiceProviderHolder.cs`
- Delete: `src/Quark.Runtime/IGrainScopeInitializerRegistry.cs`
- Delete: `src/Quark.Runtime/GrainScopeInitializerRegistry.cs`
- Test: `tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs` (add registry-only tests)

**Interfaces:**
- Consumes: nothing new.
- Produces: `Quark.Runtime.IUserServiceProviderRegistry` (`internal`, `Register(GrainType, IServiceProvider)` / `TryGet(GrainType, out IServiceProvider?)`), `Quark.Runtime.UserServiceProviderRegistry` (implementation), `Quark.Runtime.QuarkOnlyServiceProviderHolder` (`internal sealed class` with mutable `IServiceProvider? Provider { get; set; }`). Tasks 5–7 consume all three.

- [ ] **Step 1: Delete the old registry**

Delete `src/Quark.Runtime/IGrainScopeInitializerRegistry.cs` and `src/Quark.Runtime/GrainScopeInitializerRegistry.cs`.

- [ ] **Step 2: Write the failing tests**

Add to `tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs` (replace the `Placeholder` test):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class UserServiceProviderFactoryTests
{
    [Fact]
    public void UserServiceProviderRegistry_TryGet_ReturnsFalse_WhenNotRegistered()
    {
        var registry = new UserServiceProviderRegistry();
        Assert.False(registry.TryGet(new GrainType("Unregistered"), out _));
    }

    [Fact]
    public void UserServiceProviderRegistry_TryGet_ReturnsRegisteredProvider()
    {
        var registry = new UserServiceProviderRegistry();
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var grainType = new GrainType("Widget");

        registry.Register(grainType, provider);

        Assert.True(registry.TryGet(grainType, out IServiceProvider? found));
        Assert.Same(provider, found);
    }

    [Fact]
    public void UserServiceProviderRegistry_Register_Throws_OnNullProvider()
    {
        var registry = new UserServiceProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(new GrainType("Widget"), null!));
    }

    [Fact]
    public void QuarkOnlyServiceProviderHolder_DefaultsToNull()
    {
        Assert.Null(new QuarkOnlyServiceProviderHolder().Provider);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~UserServiceProviderFactoryTests"`
Expected: FAIL to compile — none of the three new types exist yet.

- [ ] **Step 3: Implement**

Create `src/Quark.Runtime/IUserServiceProviderRegistry.cs`:

```csharp
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

internal interface IUserServiceProviderRegistry
{
    void Register(GrainType grainType, IServiceProvider provider);

    bool TryGet(GrainType grainType, out IServiceProvider? provider);
}
```

Create `src/Quark.Runtime/UserServiceProviderRegistry.cs`:

```csharp
using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

internal sealed class UserServiceProviderRegistry : IUserServiceProviderRegistry
{
    private readonly ConcurrentDictionary<GrainType, IServiceProvider> _providers = new();

    public void Register(GrainType grainType, IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[grainType] = provider;
    }

    public bool TryGet(GrainType grainType, out IServiceProvider? provider)
        => _providers.TryGetValue(grainType, out provider);
}
```

Create `src/Quark.Runtime/QuarkOnlyServiceProviderHolder.cs`:

```csharp
namespace Quark.Runtime;

/// <summary>
///     Mutable holder for the lazily-built Quark-only satellite IServiceProvider (see
///     SiloHostedService.ApplyUserServiceProviderFactoryRegistrations). Registered as a singleton so
///     GrainActivation can read it without a constructor signature change; null when no behavior in the
///     process has opted into IGrainUserServiceProviderFactory.
/// </summary>
internal sealed class QuarkOnlyServiceProviderHolder
{
    public IServiceProvider? Provider { get; set; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~UserServiceProviderFactoryTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Quark.Runtime/IUserServiceProviderRegistry.cs src/Quark.Runtime/UserServiceProviderRegistry.cs \
        src/Quark.Runtime/QuarkOnlyServiceProviderHolder.cs \
        src/Quark.Runtime/IGrainScopeInitializerRegistry.cs src/Quark.Runtime/GrainScopeInitializerRegistry.cs \
        tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs
git commit -m "Add IUserServiceProviderRegistry and QuarkOnlyServiceProviderHolder, remove old registry"
```

---

## Task 4: `IBehaviorResolver`/`BehaviorResolver`/`GrainScopeBinder` — explicit construction provider

This fixes a real correctness bug in the naive design (spec §3): `BehaviorResolver` captures its OWN
ambient `IServiceProvider` in its constructor today; if resolved from a Quark-only scope, it would use
that scope alone to construct behaviors, starving user-owned constructor parameters. The fix: `Resolve`
takes the construction provider explicitly, decoupled from whichever provider resolved `BehaviorResolver`
itself. This task is independent of the opt-in feature — it's a prerequisite refactor.

**Files:**
- Modify: `src/Quark.Runtime/BehaviorResolver.cs`
- Modify: `src/Quark.Runtime/IBehaviorResolver.cs`
- Modify: `src/Quark.Runtime/GrainScopeBinder.cs`
- Modify: `tests/Quark.Tests.Unit/Runtime/BehaviorResolverTests.cs`

**Interfaces:**
- Consumes: nothing new from Tasks 1–3.
- Produces: `IBehaviorResolver.Resolve(GrainType grainType, IServiceProvider services)` (was `Resolve(GrainType grainType)`); `GrainScopeBinder.BindAndResolve(IServiceProvider bindingServices, IServiceProvider constructionServices, GrainActivation activation)` returning `IGrainBehavior` (was `async BindAndResolveAsync(IServiceProvider sp, GrainActivation activation, CancellationToken ct)` returning `ValueTask<IGrainBehavior>`). Task 7 (`GrainActivation.RunActivationAsync`) is the only other caller and is updated there.

- [ ] **Step 1: Update the existing tests to the new signature (will fail to compile until Step 3)**

Edit `tests/Quark.Tests.Unit/Runtime/BehaviorResolverTests.cs` — change every constructor call and every `.Resolve(...)` call:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class BehaviorResolverTests
{
    [Fact]
    public void Resolve_UsesRegisteredFactory_NeverReflection()
    {
        // Widget is deliberately NOT registered in DI. If BehaviorResolver fell back to
        // ActivatorUtilities.CreateInstance (reflection) here, resolving WidgetBehavior's
        // constructor parameter would throw. Success proves the factory path was used.
        var services = new ServiceCollection();
        var typeRegistry = new GrainTypeRegistry();
        var factoryRegistry = new GrainBehaviorFactoryRegistry();
        var grainType = new GrainType("Widget");

        factoryRegistry.Register(grainType, static _ => new WidgetBehavior(new Widget(42)));

        using ServiceProvider provider = services.BuildServiceProvider();
        var resolver = new BehaviorResolver(typeRegistry, factoryRegistry);

        var behavior = Assert.IsType<WidgetBehavior>(resolver.Resolve(grainType, provider));
        Assert.Equal(42, behavior.Widget.Value);
    }

    [Fact]
    public void Resolve_FallsBackToReflection_WhenNoFactoryRegistered()
    {
        var services = new ServiceCollection();
        var typeRegistry = new GrainTypeRegistry();
        var factoryRegistry = new GrainBehaviorFactoryRegistry();
        var grainType = new GrainType("PlainCounter");
        typeRegistry.Register(grainType, typeof(PlainCounterBehavior));

        using ServiceProvider provider = services.BuildServiceProvider();
        var resolver = new BehaviorResolver(typeRegistry, factoryRegistry);

        Assert.IsType<PlainCounterBehavior>(resolver.Resolve(grainType, provider));
    }

    [Fact]
    public void Resolve_Throws_WhenGrainTypeUnknown()
    {
        var services = new ServiceCollection();
        using ServiceProvider provider = services.BuildServiceProvider();
        var resolver = new BehaviorResolver(new GrainTypeRegistry(), new GrainBehaviorFactoryRegistry());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new GrainType("Missing"), provider));
    }

    private sealed class Widget(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class WidgetBehavior(Widget widget) : IGrainBehavior
    {
        public Widget Widget { get; } = widget;
    }

    private sealed class PlainCounterBehavior : IGrainBehavior;
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~BehaviorResolverTests"`
Expected: FAIL to compile — `BehaviorResolver`'s constructor still takes 3 args and `Resolve` still takes 1.

- [ ] **Step 3: Update `IBehaviorResolver`**

Edit `src/Quark.Runtime/IBehaviorResolver.cs` — full replacement:

```csharp
using Quark.Core.Abstractions.Grains;

namespace Quark.Runtime;

/// <summary>
///     Resolves the <see cref="IGrainBehavior" /> for a grain type, constructing it against an
///     explicitly-supplied <see cref="IServiceProvider" /> rather than an ambient one — so the caller
///     always controls which provider builds the behavior (the flat per-call scope by default, or a
///     composite of a Quark-only scope + a cached user provider for opted-in grain types).
/// </summary>
public interface IBehaviorResolver
{
    IGrainBehavior Resolve(GrainType grainType, IServiceProvider services);
}
```

- [ ] **Step 4: Update `BehaviorResolver`**

Edit `src/Quark.Runtime/BehaviorResolver.cs` — full replacement:

```csharp
using Quark.Core.Abstractions.Grains;

namespace Quark.Runtime;

internal sealed class BehaviorResolver(
    IGrainTypeRegistry typeRegistry,
    GrainBehaviorFactoryRegistry factoryRegistry) : IBehaviorResolver
{
    public IGrainBehavior Resolve(GrainType grainType, IServiceProvider services)
    {
        if (factoryRegistry.TryGetFactory(grainType, out Func<IServiceProvider, IGrainBehavior>? factory) &&
            factory is not null)
        {
            return factory(services);
        }

        if (!typeRegistry.TryGetGrainClass(grainType, out Type? type) || type is null)
        {
            throw new InvalidOperationException(
                $"No behavior registered for grain type '{grainType.Value}'.");
        }

#pragma warning disable IL2026 // Fallback only reached for hand-wired (non-generator) behavior registrations.
        return ReflectionBehaviorActivator.Create(services, type);
#pragma warning restore IL2026
    }
}
```

- [ ] **Step 5: Update `GrainScopeBinder`**

Edit `src/Quark.Runtime/GrainScopeBinder.cs` — full replacement:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

internal static class GrainScopeBinder
{
    /// <param name="bindingServices">
    ///     Provider used to bind the shell accessor and call context — always Quark's own scope
    ///     (the flat scope by default, or the small Quark-only scope for opted-in grain types).
    /// </param>
    /// <param name="constructionServices">
    ///     Provider used to construct the behavior instance — the same as <paramref name="bindingServices"/>
    ///     by default, or a composite of the Quark-only scope + a cached user provider for opted-in
    ///     grain types.
    /// </param>
    public static IGrainBehavior BindAndResolve(
        IServiceProvider bindingServices,
        IServiceProvider constructionServices,
        GrainActivation activation)
    {
        ((ActivationShellAccessor)bindingServices.GetRequiredService<IActivationShellAccessor>()).Shell = activation;

        ICallContextSetter callContextSetter = bindingServices.GetRequiredService<ICallContextSetter>();
        callContextSetter.Set(activation.GrainId);
        callContextSetter.SetIdempotencyKey(QuarkRequestContext.IdempotencyKey);

        return bindingServices.GetRequiredService<IBehaviorResolver>().Resolve(activation.GrainType, constructionServices);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~BehaviorResolverTests"`
Expected: PASS (3 tests). Note: `GrainActivation.cs` (Task 7) and `RuntimeServiceCollectionExtensions.cs`/`SiloHostedService.cs` (Tasks 5–6) still reference the OLD `BindAndResolveAsync`/removed types — the full solution won't build again until Task 7 completes. Run the filtered test above, not a full build, to confirm this task in isolation.

- [ ] **Step 7: Commit**

```bash
git add src/Quark.Runtime/BehaviorResolver.cs src/Quark.Runtime/IBehaviorResolver.cs \
        src/Quark.Runtime/GrainScopeBinder.cs tests/Quark.Tests.Unit/Runtime/BehaviorResolverTests.cs
git commit -m "BehaviorResolver: take construction provider explicitly, fix ambient-scope capture bug"
```

---

## Task 5: `RuntimeServiceCollectionExtensions.cs` — new registration surface

**Files:**
- Modify: `src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs`
- Test: `tests/Quark.Tests.Unit/Runtime/AddGrainBehaviorFactoryOverloadTests.cs`

**Interfaces:**
- Consumes: `IGrainUserServiceProviderFactory` (Task 1), `IUserServiceProviderRegistry`, `QuarkOnlyServiceProviderHolder` (Task 3).
- Produces: `public static IServiceCollection AddQuarkOwnedScoped<TService>(this IServiceCollection, Func<IServiceProvider, TService> factory) where TService : class`; `public static IServiceCollection AddGrainUserServiceProviderFactory<TInterface, TBehavior>(this IServiceCollection, string? behaviorId = null)`; `internal interface IQuarkOwnedServiceRegistration { void Apply(IServiceCollection satelliteServices); }`; `internal interface IUserServiceProviderFactoryRegistration { void Apply(IUserServiceProviderRegistry registry, IServiceProvider rootServices); }`. Task 6 (`SiloHostedService`) consumes both marker interfaces by name (`RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration`, `RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration`) exactly as the existing `IGrainBehaviorRegistration` pattern is consumed today. Task 9 (generator) emits calls to `AddQuarkOwnedScoped<T>` and `AddGrainUserServiceProviderFactory<TInterface,TBehavior>` by these exact names.

- [ ] **Step 1: Write the failing test**

Edit `tests/Quark.Tests.Unit/Runtime/AddGrainBehaviorFactoryOverloadTests.cs` — replace the
`AddGrainScopeInitializer_WithMatchingBehaviorId_RegistersUnderSameKeyAsAddGrainBehavior` test (which
references the deleted `AddGrainScopeInitializer`/`IGrainScopeInitializerRegistry`) with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class AddGrainBehaviorFactoryOverloadTests
{
    [Fact]
    public void AddGrainBehavior_WithExplicitBehaviorIdAndFactory_RegistersBothWithoutReflection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "factory-overload";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();

        // Widget is deliberately never registered in DI.
        services.AddGrainBehavior<IWidgetGrain, WidgetBehavior>(
            behaviorId: "custom-widget-id",
            factory: static _ => new WidgetBehavior(new Widget(7)));

        using ServiceProvider provider = services.BuildServiceProvider();

        var typeRegistry = provider.GetRequiredService<GrainTypeRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration>())
        {
            reg.Apply(typeRegistry);
        }

        var factoryRegistry = provider.GetRequiredService<GrainBehaviorFactoryRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration>())
        {
            reg.Apply(factoryRegistry);
        }

        var expectedGrainType = new GrainType("custom-widget-id");
        Assert.True(typeRegistry.TryGetGrainClass(expectedGrainType, out Type? clrType));
        Assert.Equal(typeof(WidgetBehavior), clrType);

        Assert.True(factoryRegistry.TryGetFactory(expectedGrainType, out var factory));
        var behavior = Assert.IsType<WidgetBehavior>(factory!(provider));
        Assert.Equal(7, behavior.Widget.Value);
    }

    [Fact]
    public void AddGrainUserServiceProviderFactory_WithMatchingBehaviorId_RegistersUnderSameKeyAsAddGrainBehavior()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "user-service-provider-factory-key-alignment";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();

        services.AddGrainBehavior<IWidgetGrain, OptedInWidgetBehavior>(
            behaviorId: "custom-widget-id",
            factory: static sp => new OptedInWidgetBehavior(new Widget(7)));
        services.AddGrainUserServiceProviderFactory<IWidgetGrain, OptedInWidgetBehavior>(
            behaviorId: "custom-widget-id");

        using ServiceProvider provider = services.BuildServiceProvider();

        var registry = new UserServiceProviderRegistry();
        foreach (RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration>())
        {
            reg.Apply(registry, provider);
        }

        Assert.True(registry.TryGet(new GrainType("custom-widget-id"), out IServiceProvider? found));
        Assert.Same(provider, found);
    }

    [Fact]
    public void AddQuarkOwnedScoped_RegistersServiceAndCapturesMarker()
    {
        var services = new ServiceCollection();
        services.AddQuarkOwnedScoped<Widget>(static _ => new Widget(9));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(9, provider.GetRequiredService<Widget>().Value);

        var satellite = new ServiceCollection();
        foreach (RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration marker in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration>())
        {
            marker.Apply(satellite);
        }

        using ServiceProvider satelliteProvider = satellite.BuildServiceProvider();
        Assert.Equal(9, satelliteProvider.GetRequiredService<Widget>().Value);
    }

    private interface IWidgetGrain : IGrain
    {
    }

    private sealed class Widget(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class WidgetBehavior(Widget widget) : IGrainBehavior, IWidgetGrain
    {
        public Widget Widget { get; } = widget;
    }

    private sealed class OptedInWidgetBehavior(Widget widget) : IGrainBehavior, IWidgetGrain, IGrainUserServiceProviderFactory
    {
        public Widget Widget { get; } = widget;

        public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices) => rootServices;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~AddGrainBehaviorFactoryOverloadTests"`
Expected: FAIL to compile — `AddGrainUserServiceProviderFactory`, `AddQuarkOwnedScoped`, `IUserServiceProviderFactoryRegistration`, `IQuarkOwnedServiceRegistration` don't exist yet.

- [ ] **Step 3: Remove the old `AddGrainScopeInitializer` family**

In `src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs`, delete:
- The doc comment + method `AddGrainScopeInitializer<TInterface, TBehavior>` (the block starting
  `/// <summary>\n    ///     Registers a delegate that configures this grain type's per-call scope...`
  through the closing brace of that method).
- The nested `internal interface IGrainScopeInitializerRegistration { void Apply(IGrainScopeInitializerRegistry registry); }`.
- The nested `private sealed class GrainScopeInitializerRegistration(GrainType grainType, GrainScopeInitializer initializer) : IGrainScopeInitializerRegistration { public void Apply(IGrainScopeInitializerRegistry registry) => registry.Register(grainType, initializer); }`.

- [ ] **Step 4: Replace the registry registration line in `AddQuarkRuntime()`**

Change:

```csharp
        services.TryAddSingleton<IGrainScopeInitializerRegistry, GrainScopeInitializerRegistry>();
```

to:

```csharp
        services.TryAddSingleton<IUserServiceProviderRegistry, UserServiceProviderRegistry>();
        services.TryAddSingleton<QuarkOnlyServiceProviderHolder>();
```

- [ ] **Step 5: Add `AddQuarkOwnedScoped<TService>` and its marker**

Add this public method near `AddManagedActivationMemory<T>`/`AddEagerActivationMemory<T>` (after
`AddGrainPlacementStrategy<TBehavior>`, before `AddManagedActivationMemory<T>`):

```csharp
    /// <summary>
    ///     Registers a Quark-owned scoped service AND captures a replayable marker so it can be
    ///     reconstructed onto a separate "Quark-only" satellite <see cref="IServiceCollection" /> at
    ///     startup (see <see cref="IGrainUserServiceProviderFactory" />). Used by the source generator
    ///     for per-behavior accessor registrations (<c>IActivationMemory&lt;T&gt;</c> etc.) — every
    ///     assembly's accessors become replayable this way, whether or not any behavior opts in.
    /// </summary>
    public static IServiceCollection AddQuarkOwnedScoped<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> factory)
        where TService : class
    {
        services.AddScoped(factory);
        services.AddSingleton<IQuarkOwnedServiceRegistration>(new QuarkOwnedServiceRegistration<TService>(factory));
        return services;
    }
```

- [ ] **Step 6: Add `AddGrainUserServiceProviderFactory<TInterface, TBehavior>`**

Add this public method right after `AddGrainBehavior<TInterface, TBehavior>(string, Func<...>)` (where
`AddGrainScopeInitializer` used to be):

```csharp
    /// <summary>
    ///     Registers <typeparamref name="TBehavior"/>'s <see cref="IGrainUserServiceProviderFactory" />
    ///     opt-in. Called by the generated <c>QuarkRegistrations.g.cs</c> path with an explicit
    ///     <paramref name="behaviorId"/> always supplied; use this overload directly for hand-wired
    ///     (non-generator) test/sample registrations too.
    /// </summary>
    /// <param name="behaviorId">
    ///     Explicit grain type key. Must match the <c>behaviorId</c> passed to the corresponding
    ///     <see cref="AddGrainBehavior{TInterface,TBehavior}"/> call — otherwise this registers under a
    ///     different key and silently never applies. When <c>null</c>, falls back to reflecting
    ///     <see cref="GrainBehaviorAttribute"/> or the interface name, exactly as
    ///     <see cref="AddGrainBehavior{TInterface,TBehavior}"/> does when its own <c>behaviorId</c> is
    ///     omitted.
    /// </param>
    public static IServiceCollection AddGrainUserServiceProviderFactory<TInterface, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>(
        this IServiceCollection services,
        string? behaviorId = null)
        where TInterface : IGrain
        where TBehavior : class, IGrainBehavior, TInterface, IGrainUserServiceProviderFactory
    {
#pragma warning disable IL2026 // Fallback only reached for hand-wired (non-generator) registrations.
        string key = behaviorId ?? GetGrainTypeKey<TInterface, TBehavior>();
#pragma warning restore IL2026
        services.AddSingleton<IUserServiceProviderFactoryRegistration>(
            new UserServiceProviderFactoryRegistration(new GrainType(key), TBehavior.CreateUserServiceProvider));

        return services;
    }
```

- [ ] **Step 7: Add the two new marker interfaces + implementations**

Add these next to the other `internal interface .../private sealed class ...` pairs in the
"internal deferred-registration markers" region (where `IGrainScopeInitializerRegistration` used to be):

```csharp
    internal interface IQuarkOwnedServiceRegistration
    {
        void Apply(IServiceCollection satelliteServices);
    }

    private sealed class QuarkOwnedServiceRegistration<TService>(Func<IServiceProvider, TService> factory)
        : IQuarkOwnedServiceRegistration
        where TService : class
    {
        public void Apply(IServiceCollection satelliteServices) => satelliteServices.AddScoped(factory);
    }

    internal interface IUserServiceProviderFactoryRegistration
    {
        void Apply(IUserServiceProviderRegistry registry, IServiceProvider rootServices);
    }

    private sealed class UserServiceProviderFactoryRegistration(GrainType grainType, Func<IServiceProvider, IServiceProvider> factory)
        : IUserServiceProviderFactoryRegistration
    {
        public void Apply(IUserServiceProviderRegistry registry, IServiceProvider rootServices)
            => registry.Register(grainType, factory(rootServices));
    }
```

- [ ] **Step 8: Update `AddEagerActivationMemory<T>` to use `AddQuarkOwnedScoped`**

Change its body from `services.AddScoped<IEagerActivationMemory<T>>(...)` to
`services.AddQuarkOwnedScoped<IEagerActivationMemory<T>>(...)` — same factory lambda:

```csharp
    public static IServiceCollection AddEagerActivationMemory<T>(
        this IServiceCollection services)
        where T : class
    {
        services.AddQuarkOwnedScoped<IEagerActivationMemory<T>>(static sp =>
            new EagerActivationMemoryAccessor<T>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateEagerHolder<T>()));
        return services;
    }
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~AddGrainBehaviorFactoryOverloadTests"`
Expected: PASS (3 tests). Note: `SiloHostedService.cs` and `GrainActivation.cs` still reference the removed
`ApplyScopeInitializerRegistrations`/old `GrainScopeBinder` signature — full-solution build still fails
until Tasks 6–7 complete; run the filtered test above, not a full build.

- [ ] **Step 10: Commit**

```bash
git add src/Quark.Runtime/RuntimeServiceCollectionExtensions.cs \
        tests/Quark.Tests.Unit/Runtime/AddGrainBehaviorFactoryOverloadTests.cs
git commit -m "Add AddQuarkOwnedScoped/AddGrainUserServiceProviderFactory, remove AddGrainScopeInitializer"
```

---

## Task 6: `SiloHostedService` — build the registry and satellite provider at startup

**Files:**
- Modify: `src/Quark.Runtime/SiloHostedService.cs`
- Modify: `src/Quark.Runtime/BehaviorStartupValidator.cs`

**Interfaces:**
- Consumes: `IUserServiceProviderRegistry`, `UserServiceProviderRegistry`, `QuarkOnlyServiceProviderHolder` (Task 3); `RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration`, `IQuarkOwnedServiceRegistration` (Task 5).
- Produces: `SiloHostedService.ApplyUserServiceProviderFactoryRegistrations()` (private method, called from `StartAsync` where `ApplyScopeInitializerRegistrations()` used to be called) and disposal of the satellite provider in `StopAsync`. Task 7 (`GrainActivation`) reads `IUserServiceProviderRegistry`/`QuarkOnlyServiceProviderHolder` off `_root` — this task is what populates them.

**Why `BehaviorStartupValidator` needs a change:** `AddQuarkRuntime()` registers hosted services in this
order: `GrainIdleCollector`, `BehaviorStartupValidator`, `SiloHostedService`. .NET's generic host runs
`IHostedService.StartAsync` in registration order, so `BehaviorStartupValidator.StartAsync` runs **before**
`SiloHostedService.StartAsync` — meaning `IUserServiceProviderRegistry`/`QuarkOnlyServiceProviderHolder`
are NOT yet populated when the validator runs. Validating an opted-in behavior today's way (`root.CreateScope()`
against the flat root) would be a false-positive startup failure for any behavior whose
`CreateUserServiceProvider` deliberately does NOT rely on `silo.Services` registrations. Skip validation
for those behaviors instead of validating them against the wrong provider.

- [ ] **Step 1: Write the failing test for the satellite-provider build**

There is no existing dedicated test file for `SiloHostedService`'s registration-application methods (they're
exercised indirectly via `GrainScopeInitializerTests.cs`'s `ApplyRegistrations` helper, which manually
replicated the same logic rather than calling `SiloHostedService` directly). This task's behavior is
covered end-to-end in Task 8 (`UserServiceProviderFactoryTests.cs`, which drives a real
`LocalGrainCallInvoker`/`SiloHostedService`-equivalent flow) rather than a standalone unit test here — write
the implementation now; Task 8 is the test for it. This mirrors how the original `GrainScopeInitializerRegistry`
population was only ever tested indirectly through the same kind of end-to-end test.

- [ ] **Step 2: Replace `ApplyScopeInitializerRegistrations` in `SiloHostedService.cs`**

In `src/Quark.Runtime/SiloHostedService.cs`, change the call in `StartAsync`:

```csharp
        // Apply deferred per-call scope initializers (AddGrainScopeInitializer<TInterface, TBehavior> calls).
        ApplyScopeInitializerRegistrations();
```

to:

```csharp
        // Apply deferred user-service-provider-factory registrations (AddGrainUserServiceProviderFactory calls).
        ApplyUserServiceProviderFactoryRegistrations();
```

Then replace the `ApplyScopeInitializerRegistrations()` method body entirely with:

```csharp
    private void ApplyUserServiceProviderFactoryRegistrations()
    {
        if (_services.GetService<IUserServiceProviderRegistry>() is not { } registry)
        {
            return;
        }

        var factoryRegistrations = _services
            .GetServices<RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration>()
            .ToList();

        foreach (RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration reg in factoryRegistrations)
        {
            reg.Apply(registry, _services);
        }

        if (factoryRegistrations.Count == 0)
        {
            return;
        }

        GrainTypeRegistry mainTypeRegistry = _services.GetRequiredService<GrainTypeRegistry>();
        GrainBehaviorFactoryRegistry mainFactoryRegistry = _services.GetRequiredService<GrainBehaviorFactoryRegistry>();

        var quarkOnly = new ServiceCollection();
        quarkOnly.AddSingleton(mainTypeRegistry);
        quarkOnly.AddSingleton<IGrainTypeRegistry>(mainTypeRegistry);
        quarkOnly.AddSingleton(mainFactoryRegistry);
        quarkOnly.AddScoped<ActivationShellAccessor>();
        quarkOnly.AddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
        quarkOnly.AddScoped<CallContext>();
        quarkOnly.AddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
        quarkOnly.AddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
        quarkOnly.AddScoped<IBehaviorResolver, BehaviorResolver>();

        foreach (RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration marker in
                 _services.GetServices<RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration>())
        {
            marker.Apply(quarkOnly);
        }

        _services.GetRequiredService<QuarkOnlyServiceProviderHolder>().Provider = quarkOnly.BuildServiceProvider();
    }
```

- [ ] **Step 3: Dispose the satellite provider on shutdown**

In `StopAsync`, right after the existing `GrainActivationTable` drain block (`if (_services.GetService<GrainActivationTable>() is { } table) { ... }`), add:

```csharp
        if (_services.GetService<QuarkOnlyServiceProviderHolder>()?.Provider is IAsyncDisposable quarkOnlyProvider)
        {
            await quarkOnlyProvider.DisposeAsync().ConfigureAwait(false);
        }
```

- [ ] **Step 4: Skip opted-in behaviors in `BehaviorStartupValidator`**

Edit `src/Quark.Runtime/BehaviorStartupValidator.cs` — inside the `foreach` loop in `StartAsync`, add a
skip check right after the loop variable is bound:

```csharp
        foreach ((GrainType grainType, Type behaviorType) in typeRegistry.GetAll())
        {
            if (typeof(IGrainUserServiceProviderFactory).IsAssignableFrom(behaviorType))
            {
                // Opted-in behaviors are constructed against a composite of a Quark-only scope + a
                // cached user provider built later in SiloHostedService.StartAsync (which runs AFTER
                // this hosted service, per AddQuarkRuntime()'s hosted-service registration order).
                // Validating against the flat root here would produce false-positive startup failures
                // for behaviors whose CreateUserServiceProvider doesn't rely on silo.Services at all.
                logger.LogDebug(
                    "Behavior {Type} skipped DI validation (opts into IGrainUserServiceProviderFactory)",
                    behaviorType.Name);
                continue;
            }

            try
            {
```

(The existing `try { ... } catch { ... }` block and its closing brace stay as-is — this just adds the
`if`/`continue` guard before the `try`.)

- [ ] **Step 5: Build to confirm this task compiles in isolation**

Run: `dotnet build src/Quark.Runtime/Quark.Runtime.csproj`
Expected: Still FAILS — `GrainActivation.cs` (Task 7) still calls the old `GrainScopeBinder.BindAndResolveAsync`
signature and doesn't yet branch on the new registry/holder. Confirm the ONLY remaining errors are in
`GrainActivation.cs`.

- [ ] **Step 6: Commit**

```bash
git add src/Quark.Runtime/SiloHostedService.cs src/Quark.Runtime/BehaviorStartupValidator.cs
git commit -m "SiloHostedService: build user-service-provider registry and Quark-only satellite at startup"
```

---

## Task 7: `GrainActivation.RunActivationAsync` — branch on the opt-in path

**Files:**
- Modify: `src/Quark.Runtime/GrainActivation.cs:881-891`

**Interfaces:**
- Consumes: `IUserServiceProviderRegistry`, `QuarkOnlyServiceProviderHolder` (Task 3), `CompositeServiceProvider` (Task 2), `GrainScopeBinder.BindAndResolve` (Task 4).
- Produces: the completed `RunActivationAsync` — no further tasks build on this directly, but Task 8's tests exercise it end-to-end.

- [ ] **Step 1: There is no isolated unit test for this method today**

`RunActivationAsync` is `internal` and exercised only through `LocalGrainCallInvoker`/`GrainActivationTable`
integration flows — exactly like the original scope-initializer behavior, which was tested via
`GrainScopeInitializerTests.cs` driving a real `LocalGrainCallInvoker`. Task 8 is the test for this method;
implement it now.

- [ ] **Step 2: Replace `RunActivationAsync`**

In `src/Quark.Runtime/GrainActivation.cs`, replace the method (currently at lines 881-891):

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

with:

```csharp
    internal async Task RunActivationAsync(CancellationToken ct)
    {
        IUserServiceProviderRegistry registry = _root.GetRequiredService<IUserServiceProviderRegistry>();
        QuarkOnlyServiceProviderHolder holder = _root.GetRequiredService<QuarkOnlyServiceProviderHolder>();

        bool useQuarkOnlyScope = holder.Provider is not null &&
            registry.TryGet(GrainType, out IServiceProvider? userProvider) && userProvider is not null;

        using IServiceScope scope = useQuarkOnlyScope ? holder.Provider!.CreateScope() : _root.CreateScope();
        IServiceProvider constructionServices = useQuarkOnlyScope
            ? new CompositeServiceProvider(scope.ServiceProvider, userProvider!)
            : scope.ServiceProvider;

        IGrainBehavior behavior = GrainScopeBinder.BindAndResolve(scope.ServiceProvider, constructionServices, this);
        await RunEagerInitAsync(constructionServices, ct).ConfigureAwait(false);
        if (behavior is IActivationLifecycle lifecycle)
        {
            await lifecycle.OnActivateAsync(ct).ConfigureAwait(false);
        }
    }
```

Update the doc comment immediately above the method (currently: `// Runs the full activation sequence in a
single scope: ...`) to:

```csharp
    // Runs the full activation sequence:
    // 1. Bind shell accessor + call context, using the Quark-only scope for opted-in grain types
    //    (see IUserServiceProviderRegistry/QuarkOnlyServiceProviderHolder) or the flat scope otherwise.
    // 2. Resolve behavior (ctor fires; any IEagerActivationMemory<T>.Load() calls register factories).
    // 3. Initialize all eager holders with the construction provider BEFORE OnActivateAsync.
    // 4. Call OnActivateAsync if the behavior implements IActivationLifecycle.
```

- [ ] **Step 3: Full solution build**

Run: `dotnet build Quark.slnx`
Expected: SUCCEEDS. This is the first point since Task 1 where the whole solution should compile —
confirm no remaining references to `GrainScopeInitializer`, `IGrainScopeInitializerRegistry`,
`GrainScopeInitializerRegistry`, `AddGrainScopeInitializer`, or the old 1-arg `IBehaviorResolver.Resolve`/
`GrainScopeBinder.BindAndResolveAsync` signatures anywhere in the solution.

- [ ] **Step 4: Run the full unit test suite**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj`
Expected: PASS — including all existing tests that exercise `RunActivationAsync` indirectly (activation
lifecycle tests, mailbox tests, etc.), since the non-opted-in path is behaviorally identical to before.

- [ ] **Step 5: Commit**

```bash
git add src/Quark.Runtime/GrainActivation.cs
git commit -m "GrainActivation: branch RunActivationAsync on the user-service-provider opt-in path"
```

---

## Task 8: End-to-end tests for the opt-in flow

**Files:**
- Modify: `tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs` (remove placeholder, add real tests)

**Interfaces:**
- Consumes: everything from Tasks 1–7.
- Produces: nothing further downstream — this is the integration-level confirmation the mechanism works end-to-end.

- [ ] **Step 1: Write the tests**

Replace the entire contents of `tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class UserServiceProviderFactoryTests
{
    [Fact]
    public void UserServiceProviderRegistry_TryGet_ReturnsFalse_WhenNotRegistered()
    {
        var registry = new UserServiceProviderRegistry();
        Assert.False(registry.TryGet(new GrainType("Unregistered"), out _));
    }

    [Fact]
    public void UserServiceProviderRegistry_TryGet_ReturnsRegisteredProvider()
    {
        var registry = new UserServiceProviderRegistry();
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var grainType = new GrainType("Widget");

        registry.Register(grainType, provider);

        Assert.True(registry.TryGet(grainType, out IServiceProvider? found));
        Assert.Same(provider, found);
    }

    [Fact]
    public void UserServiceProviderRegistry_Register_Throws_OnNullProvider()
    {
        var registry = new UserServiceProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(new GrainType("Widget"), null!));
    }

    [Fact]
    public void QuarkOnlyServiceProviderHolder_DefaultsToNull()
    {
        Assert.Null(new QuarkOnlyServiceProviderHolder().Provider);
    }

    [Fact]
    public async Task OptedInBehavior_UserFactory_RunsOnce_ReusedAcrossCalls()
    {
        var callCount = 0;
        ServiceCollection services = CreateServices();

        services.AddGrainBehavior<ICountingGrain, CountingBehavior>(
            behaviorId: "CountingGrain",
            factory: static sp => new CountingBehavior(sp.GetRequiredService<Counter>()));
        services.AddGrainUserServiceProviderFactory<ICountingGrain, CountingBehavior>(behaviorId: "CountingGrain");
        services.AddSingleton(new UserFactoryProbe(() => callCount++));

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("CountingGrain"), "counter-1");

        int first = await invoker.InvokeAsync<IncrementInvokable, int>(grainId, new IncrementInvokable(), CancellationToken.None);
        int second = await invoker.InvokeAsync<IncrementInvokable, int>(grainId, new IncrementInvokable(), CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(1, callCount); // CreateUserServiceProvider ran exactly once, not once per call.
    }

    [Fact]
    public async Task NonOptedInBehavior_IsUnaffected()
    {
        ServiceCollection services = CreateServices();
        services.AddGrainBehavior<ICountingGrain, PlainCountingBehavior>(
            behaviorId: "PlainCountingGrain",
            factory: static sp => new PlainCountingBehavior(sp.GetRequiredService<Counter>()));

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("PlainCountingGrain"), "counter-2");

        int result = await invoker.InvokeAsync<IncrementInvokable, int>(grainId, new IncrementInvokable(), CancellationToken.None);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task OptedInBehavior_QuarkServicesResolveFromEngine_NotFromUserProvider()
    {
        ServiceCollection services = CreateServices();

        // Deliberately return a provider that ALSO has ICallContext registered — a misuse scenario.
        // The engine's real per-call ICallContext must still win (structural guarantee, not convention).
        services.AddGrainBehavior<ITenantGrain, TenantBehavior>(
            behaviorId: "TenantGrain",
            factory: static sp => new TenantBehavior(sp.GetRequiredService<ICallContext>()));
        services.AddGrainUserServiceProviderFactory<ITenantGrain, TenantBehavior>(behaviorId: "TenantGrain");

        await using ServiceProvider provider = services.BuildServiceProvider();
        ApplyRegistrations(provider);

        LocalGrainCallInvoker invoker = CreateInvoker(provider);
        var grainId = new GrainId(new GrainType("TenantGrain"), "tenant-xyz");

        string result = await invoker.InvokeAsync<GetGrainKeyInvokable, string>(grainId, new GetGrainKeyInvokable(), CancellationToken.None);

        Assert.Equal("tenant-xyz", result);
    }

    [Fact]
    public void CreateUserServiceProviderThrows_FailsSiloStartup_NotFirstCall()
    {
        ServiceCollection services = CreateServices();
        services.AddGrainBehavior<ICountingGrain, ThrowingFactoryBehavior>(
            behaviorId: "ThrowingFactoryGrain",
            factory: static sp => new ThrowingFactoryBehavior(sp.GetRequiredService<Counter>()));
        services.AddGrainUserServiceProviderFactory<ICountingGrain, ThrowingFactoryBehavior>(
            behaviorId: "ThrowingFactoryGrain");

        using ServiceProvider provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<GrainTypeRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration>())
        {
            reg.Apply(registry);
        }

        var userRegistry = new UserServiceProviderRegistry();
        var factoryRegistrations = provider
            .GetServices<RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration>();

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration reg in factoryRegistrations)
            {
                reg.Apply(userRegistry, provider);
            }
        });
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "user-service-provider-factory";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddSingleton<Counter>();
        return services;
    }

    private static void ApplyRegistrations(ServiceProvider provider)
    {
        var typeRegistry = provider.GetRequiredService<GrainTypeRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration>())
        {
            reg.Apply(typeRegistry);
        }

        var factoryRegistry = provider.GetRequiredService<GrainBehaviorFactoryRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration>())
        {
            reg.Apply(factoryRegistry);
        }

        var userRegistry = provider.GetRequiredService<IUserServiceProviderRegistry>();
        var factoryRegistrations = provider
            .GetServices<RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration>()
            .ToList();
        foreach (RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration reg in factoryRegistrations)
        {
            reg.Apply(userRegistry, provider);
        }

        if (factoryRegistrations.Count > 0)
        {
            var mainTypeRegistry = provider.GetRequiredService<GrainTypeRegistry>();
            var mainFactoryRegistry = provider.GetRequiredService<GrainBehaviorFactoryRegistry>();

            var quarkOnly = new ServiceCollection();
            quarkOnly.AddSingleton(mainTypeRegistry);
            quarkOnly.AddSingleton<IGrainTypeRegistry>(mainTypeRegistry);
            quarkOnly.AddSingleton(mainFactoryRegistry);
            quarkOnly.AddScoped<ActivationShellAccessor>();
            quarkOnly.AddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
            quarkOnly.AddScoped<CallContext>();
            quarkOnly.AddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
            quarkOnly.AddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
            quarkOnly.AddScoped<IBehaviorResolver, BehaviorResolver>();

            foreach (RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration marker in
                     provider.GetServices<RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration>())
            {
                marker.Apply(quarkOnly);
            }

            provider.GetRequiredService<QuarkOnlyServiceProviderHolder>().Provider = quarkOnly.BuildServiceProvider();
        }
    }

    private static LocalGrainCallInvoker CreateInvoker(ServiceProvider provider)
        => new(
            provider.GetRequiredService<GrainActivationTable>(),
            provider.GetRequiredService<IGrainTypeRegistry>(),
            provider.GetRequiredService<IGrainDirectory>(),
            provider,
            provider.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance);

    private sealed class Counter
    {
        public int Value { get; set; }
    }

    private sealed class UserFactoryProbe(Action onCreate)
    {
        public void RecordCreate() => onCreate();
    }

    private interface ICountingGrain : IGrain
    {
        Task<int> IncrementAsync();
    }

    private sealed class CountingBehavior(Counter counter) : IGrainBehavior, ICountingGrain, IGrainUserServiceProviderFactory
    {
        public Task<int> IncrementAsync()
        {
            counter.Value++;
            return Task.FromResult(counter.Value);
        }

        public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)
        {
            rootServices.GetRequiredService<UserFactoryProbe>().RecordCreate();
            return rootServices;
        }
    }

    private sealed class PlainCountingBehavior(Counter counter) : IGrainBehavior, ICountingGrain
    {
        public Task<int> IncrementAsync()
        {
            counter.Value++;
            return Task.FromResult(counter.Value);
        }
    }

    private sealed class ThrowingFactoryBehavior(Counter counter) : IGrainBehavior, ICountingGrain, IGrainUserServiceProviderFactory
    {
        public Task<int> IncrementAsync() => Task.FromResult(counter.Value);

        public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)
            => throw new InvalidOperationException("simulated startup misconfiguration");
    }

    private interface ITenantGrain : IGrain
    {
        Task<string> GetKeyAsync();
    }

    private sealed class TenantBehavior(ICallContext ctx) : IGrainBehavior, ITenantGrain, IGrainUserServiceProviderFactory
    {
        public Task<string> GetKeyAsync() => Task.FromResult(ctx.GrainId.Key);

        public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)
        {
            // Misuse: registers a decoy ICallContext into the "user" provider. The engine's real
            // per-call ICallContext must still win via CompositeServiceProvider's Quark-first ordering.
            var decoy = new ServiceCollection();
            decoy.AddSingleton<ICallContext>(new DecoyCallContext());
            return decoy.BuildServiceProvider();
        }
    }

    private sealed class DecoyCallContext : ICallContext
    {
        public GrainId GrainId => new(new GrainType("Decoy"), "decoy-key");
    }

    private readonly struct IncrementInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 1;

        public ValueTask<int> Invoke(IGrainBehavior behavior)
            => new(((ICountingGrain)behavior).IncrementAsync());

        public void Serialize(ref CodecWriter writer) { }

        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private readonly struct GetGrainKeyInvokable : IGrainInvokable<string>
    {
        public uint MethodId => 1;

        public ValueTask<string> Invoke(IGrainBehavior behavior)
            => new(((ITenantGrain)behavior).GetKeyAsync());

        public void Serialize(ref CodecWriter writer) { }

        public string DeserializeResult(ref CodecReader reader) => reader.ReadString();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail, then pass**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~UserServiceProviderFactoryTests"`

If any test fails, diagnose against the exact mechanism in Tasks 3–7 before changing test expectations —
in particular:
- `OptedInBehavior_UserFactory_RunsOnce_ReusedAcrossCalls` depends on `ApplyRegistrations` building the
  Quark-only satellite BEFORE `CreateInvoker` drives any calls (order in the test matters).
- `OptedInBehavior_QuarkServicesResolveFromEngine_NotFromUserProvider` is the direct regression test for
  the `CompositeServiceProvider` Quark-first ordering documented in spec §3 — if this fails, check
  `CompositeServiceProvider`'s argument order in `GrainActivation.RunActivationAsync` (Task 7, Step 2):
  the Quark scope must be `primary`, the user provider `secondary`.

Expected: PASS (7 tests total, including the 4 registry tests from Task 3).

- [ ] **Step 3: Run the full unit test suite**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj`
Expected: PASS — no regressions in unrelated tests.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Tests.Unit/Runtime/UserServiceProviderFactoryTests.cs
git commit -m "Add end-to-end tests for the IGrainUserServiceProviderFactory opt-in flow"
```

---

## Task 9: `BehaviorRegistrationGenerator` — detect the opt-in interface, emit registration

**Files:**
- Modify: `src/Quark.CodeGenerator/BehaviorRegistrationGenerator.cs`
- Modify: `tests/Quark.Tests.CodeGenerator/BehaviorRegistrationGeneratorTests.cs`

**Interfaces:**
- Consumes: `AddGrainUserServiceProviderFactory<TInterface,TBehavior>` and `AddQuarkOwnedScoped<T>` (Task 5), by exact name — the generator emits calls to these.
- Produces: generated code calling `RuntimeServiceCollectionExtensions.AddGrainUserServiceProviderFactory<...>` for behaviors implementing `IGrainUserServiceProviderFactory`, and `AddQuarkOwnedScoped<T>` instead of `AddScoped<T>` for `IActivationMemory<T>`/`IManagedActivationMemory<T>` accessor emissions.

- [ ] **Step 1: Write the failing tests**

Add these tests to `tests/Quark.Tests.CodeGenerator/BehaviorRegistrationGeneratorTests.cs` (near
`Generates_IActivationMemory_Scoped_Registration`):

```csharp
    [Fact]
    public void Generates_UserServiceProviderFactory_Registration_When_Behavior_Opts_In()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task IncrementAsync();
                              }

                              public sealed class CounterBehavior : IGrainBehavior, ICounterGrain, IGrainUserServiceProviderFactory
                              {
                                  public Task IncrementAsync() => Task.CompletedTask;

                                  public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices) => rootServices;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains(
            "AddGrainUserServiceProviderFactory<global::Demo.ICounterGrain, global::Demo.CounterBehavior>(",
            generated);
        Assert.Contains("behaviorId: \"CounterGrain\");", generated);
    }

    [Fact]
    public void Does_Not_Generate_UserServiceProviderFactory_Registration_When_Behavior_Does_Not_Opt_In()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task IncrementAsync();
                              }

                              public sealed class CounterBehavior : IGrainBehavior, ICounterGrain
                              {
                                  public Task IncrementAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.DoesNotContain("AddGrainUserServiceProviderFactory<", generated);
    }

    [Fact]
    public void Generates_IActivationMemory_Registration_Via_AddQuarkOwnedScoped()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public sealed class CounterState { public int Value { get; set; } }

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task IncrementAsync();
                              }

                              public sealed class CounterBehavior : IGrainBehavior, ICounterGrain
                              {
                                  public CounterBehavior(IActivationMemory<CounterState> memory) { }
                                  public Task IncrementAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains(
            "RuntimeServiceCollectionExtensions.AddQuarkOwnedScoped<global::Quark.Core.Abstractions.Hosting.IActivationMemory<global::Demo.CounterState>>(services,",
            generated);
        Assert.DoesNotContain(
            "services.AddScoped<global::Quark.Core.Abstractions.Hosting.IActivationMemory<global::Demo.CounterState>>(",
            generated);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Quark.Tests.CodeGenerator/Quark.Tests.CodeGenerator.csproj --filter "FullyQualifiedName~BehaviorRegistrationGeneratorTests"`
Expected: FAIL — `AddGrainUserServiceProviderFactory_...` tests fail (nothing emitted); `Generates_IActivationMemory_Registration_Via_AddQuarkOwnedScoped` fails (still emits `AddScoped`, not `AddQuarkOwnedScoped`). Existing tests should still pass.

- [ ] **Step 3: Add the detection constant and model field**

In `src/Quark.CodeGenerator/BehaviorRegistrationGenerator.cs`, add a new constant alongside the existing
ones (near `IActivationMemoryNs`):

```csharp
    private const string IGrainUserServiceProviderFactoryFqn = "Quark.Core.Abstractions.Hosting.IGrainUserServiceProviderFactory";
```

In `ExtractModel`, right after the existing `grainIface` disambiguation block (after
`if (grainIfaces.Count > 1) { ... }`), add:

```csharp
        bool implementsUserServiceProviderFactory = type.AllInterfaces.Any(
            static i => i.ToDisplayString() == IGrainUserServiceProviderFactoryFqn);
```

- [ ] **Step 4: Thread the new field through `BehaviorModel`**

In the `BehaviorModel` class, add a new property and thread it through both constructors:

```csharp
        // Error-only model (QRK0050): only diagnostics are populated.
        public BehaviorModel(ImmutableArray<Diagnostic> diagnostics)
        {
            Diagnostics = diagnostics;
            BehaviorFqn = string.Empty;
            GrainInterfaceFqn = string.Empty;
            GrainTypeName = string.Empty;
            ProxyFqn = string.Empty;
            PlacementStrategyExpression = string.Empty;
            FactoryExpression = null;
            InMemoryStateTypes = ImmutableArray<string>.Empty;
            PersistentStateTypes = ImmutableArray<string>.Empty;
            ManagedStateTypes = ImmutableArray<string>.Empty;
            EagerStateTypes = ImmutableArray<string>.Empty;
            ImplicitStreamNamespaces = ImmutableArray<string>.Empty;
            PersistentStateSlots = ImmutableArray<PersistentStateSlot>.Empty;
            ImplementsUserServiceProviderFactory = false;
        }

        public BehaviorModel(
            string behaviorFqn,
            string grainInterfaceFqn,
            string grainTypeName,
            string proxyFqn,
            string placementStrategyExpression,
            string? factoryExpression,
            ImmutableArray<string> inMemoryStateTypes,
            ImmutableArray<string> persistentStateTypes,
            ImmutableArray<string> managedStateTypes,
            ImmutableArray<string> eagerStateTypes,
            ImmutableArray<string> implicitStreamNamespaces,
            ImmutableArray<PersistentStateSlot> persistentStateSlots,
            bool implementsUserServiceProviderFactory,
            ImmutableArray<Diagnostic> diagnostics)
        {
            BehaviorFqn = behaviorFqn;
            GrainInterfaceFqn = grainInterfaceFqn;
            GrainTypeName = grainTypeName;
            ProxyFqn = proxyFqn;
            PlacementStrategyExpression = placementStrategyExpression;
            FactoryExpression = factoryExpression;
            InMemoryStateTypes = inMemoryStateTypes;
            PersistentStateTypes = persistentStateTypes;
            ManagedStateTypes = managedStateTypes;
            EagerStateTypes = eagerStateTypes;
            ImplicitStreamNamespaces = implicitStreamNamespaces;
            PersistentStateSlots = persistentStateSlots;
            ImplementsUserServiceProviderFactory = implementsUserServiceProviderFactory;
            Diagnostics = diagnostics;
        }

        public bool IsValid => !string.IsNullOrEmpty(BehaviorFqn);
        public string BehaviorFqn { get; }
        public string GrainInterfaceFqn { get; }
        public string GrainTypeName { get; }
        public string ProxyFqn { get; }
        public string PlacementStrategyExpression { get; }
        public string? FactoryExpression { get; }
        public ImmutableArray<string> InMemoryStateTypes { get; }
        public ImmutableArray<string> PersistentStateTypes { get; }
        public ImmutableArray<string> ManagedStateTypes { get; }
        public ImmutableArray<string> EagerStateTypes { get; }
        public ImmutableArray<string> ImplicitStreamNamespaces { get; }
        public ImmutableArray<PersistentStateSlot> PersistentStateSlots { get; }
        public bool ImplementsUserServiceProviderFactory { get; }
        public ImmutableArray<Diagnostic> Diagnostics { get; }
```

Update the single call site in `ExtractModel` (the `return new BehaviorModel(...)` at the end) to pass the
new argument in the matching position (right before `diagnostics`):

```csharp
        return new BehaviorModel(
            behaviorFqn: behaviorFqn,
            grainInterfaceFqn: grainIfaceFqn,
            grainTypeName: grainTypeName,
            proxyFqn: proxyFqn,
            placementStrategyExpression: placementStrategyExpression,
            factoryExpression: factoryExpression,
            inMemoryStateTypes: inMemory.Distinct().ToImmutableArray(),
            persistentStateTypes: persistent.Distinct().ToImmutableArray(),
            managedStateTypes: managed.Distinct().ToImmutableArray(),
            eagerStateTypes: eager.Distinct().ToImmutableArray(),
            implicitStreamNamespaces: implicitNamespaces.Distinct().ToImmutableArray(),
            persistentStateSlots: persistentSlots.Distinct().ToImmutableArray(),
            implementsUserServiceProviderFactory: implementsUserServiceProviderFactory,
            diagnostics: diagList.ToImmutableArray());
```

- [ ] **Step 5: Emit the registration call**

In the `Emit` method's per-behavior loop (the `foreach (BehaviorModel m in valid)` block that emits
`AddGrainBehavior`/`AddGrainPlacementStrategy`/`AddGrainTransportDispatcher`), add right after the
`AddGrainTransportDispatcher` emission:

```csharp
            if (m.ImplementsUserServiceProviderFactory)
            {
                sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddGrainUserServiceProviderFactory<{m.GrainInterfaceFqn}, {m.BehaviorFqn}>(");
                sb.AppendLine($"            services, behaviorId: \"{m.GrainTypeName}\");");
            }
```

- [ ] **Step 6: Switch `IActivationMemory<T>`/`IManagedActivationMemory<T>` emissions to `AddQuarkOwnedScoped`**

Change the `IActivationMemory<T>` emission block from:

```csharp
        foreach (string tArg in inMemoryStates)
        {
            sb.AppendLine($"        services.AddScoped<global::Quark.Core.Abstractions.Hosting.IActivationMemory<{tArg}>>(static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.ActivationMemoryAccessor<{tArg}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>()");
            sb.AppendLine($"                  .Shell.GetOrCreateHolder<{tArg}>()));");
        }
```

to:

```csharp
        foreach (string tArg in inMemoryStates)
        {
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddQuarkOwnedScoped<global::Quark.Core.Abstractions.Hosting.IActivationMemory<{tArg}>>(services, static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.ActivationMemoryAccessor<{tArg}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>()");
            sb.AppendLine($"                  .Shell.GetOrCreateHolder<{tArg}>()));");
        }
```

Change the `IManagedActivationMemory<T>` emission block from:

```csharp
        foreach (string tArg in managedStates)
        {
            sb.AppendLine($"        services.AddScoped<global::Quark.Core.Abstractions.Hosting.IManagedActivationMemory<{tArg}>>(static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.ManagedActivationMemoryAccessor<{tArg}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>()");
            sb.AppendLine($"                  .Shell.GetOrCreateManagedHolder<{tArg}>()));");
        }
```

to:

```csharp
        foreach (string tArg in managedStates)
        {
            sb.AppendLine($"        global::Quark.Runtime.RuntimeServiceCollectionExtensions.AddQuarkOwnedScoped<global::Quark.Core.Abstractions.Hosting.IManagedActivationMemory<{tArg}>>(services, static sp =>");
            sb.AppendLine($"            new global::Quark.Persistence.Abstractions.ManagedActivationMemoryAccessor<{tArg}>(");
            sb.AppendLine($"                sp.GetRequiredService<global::Quark.Runtime.IActivationShellAccessor>()");
            sb.AppendLine($"                  .Shell.GetOrCreateManagedHolder<{tArg}>()));");
        }
```

`IPersistentActivationMemory<T>` (lines around 452-459) and `IPersistentState<T>` slots (lines around
490-495) are **intentionally left unchanged** — still plain `services.AddScoped<...>(...)` — per the v1
non-goal (spec §2): they need `IStorage<T>`/`IGrainStorage` from separate packages not covered here.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Quark.Tests.CodeGenerator/Quark.Tests.CodeGenerator.csproj --filter "FullyQualifiedName~BehaviorRegistrationGeneratorTests"`
Expected: PASS — all new tests plus every pre-existing test in the file (the `IPersistentActivationMemory`
scoped-registration test must still pass unchanged, confirming persistence emissions were untouched).

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test Quark.slnx`
Expected: PASS across all test projects.

- [ ] **Step 9: Commit**

```bash
git add src/Quark.CodeGenerator/BehaviorRegistrationGenerator.cs \
        tests/Quark.Tests.CodeGenerator/BehaviorRegistrationGeneratorTests.cs
git commit -m "Generator: detect IGrainUserServiceProviderFactory, emit via AddQuarkOwnedScoped"
```

---

## Task 10: AOT smoke build and documentation

**Files:**
- No new AOT-smoke host — confirmed via `.github/workflows/ci.yml:47` that the repo's existing "Native AOT
  smoke publish" step runs `dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -f net10.0 -c Release -r
  ${{ matrix.rid }} /p:PublishAot=true` — it AOT-publishes the `Quark.Runtime` **library** itself (confirming
  every type in that assembly, including the ones this plan adds, trims/AOTs clean), not a sample host with
  generator-driven behaviors. No existing CI step exercises generator-emitted code
  (`AddGrainUserServiceProviderFactory`/`AddQuarkOwnedScoped` calls) under `PublishAot=true` at all — this
  is a **pre-existing gap**, not something this change introduces, so adding a new generator-consuming AOT
  smoke sample to CI is explicitly **out of scope** here (a CI workflow change should be its own,
  separately-reviewed task). This task only re-runs the existing command to confirm no regression.
- Modify: `FEATURES.md`
- Modify: `wiki/Source-Generators.md`

**Interfaces:**
- Consumes: everything from Tasks 1–9.
- Produces: nothing further — this is the closing documentation/verification task.

- [ ] **Step 1: Run the existing AOT smoke publish locally**

Run: `dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -f net10.0 -c Release -r linux-x64 /p:PublishAot=true`
Expected: SUCCEEDS with no new `RequiresUnreferencedCode`/`RequiresDynamicCode`/trim warnings attributable
to this change — confirms `CompositeServiceProvider`, `UserServiceProviderRegistry`,
`QuarkOnlyServiceProviderHolder`, and the modified `BehaviorResolver`/`GrainScopeBinder`/
`SiloHostedService`/`GrainActivation`/`BehaviorStartupValidator` don't introduce new AOT diagnostics in the
`Quark.Runtime` assembly itself.

- [ ] **Step 2: Update `FEATURES.md`**

Find the row/section covering grain-scope/DI extensibility (search `grep -n "scope initializer\|ScopeInitializer" FEATURES.md`)
and update it to describe `IGrainUserServiceProviderFactory` as the current mechanism, noting the
`GrainScopeInitializer` family was removed in favor of it, with a one-line pointer to
`docs/superpowers/specs/2026-07-10-grain-user-service-provider-factory-design.md`.

- [ ] **Step 3: Update `wiki/Source-Generators.md`**

Add a short section documenting the new generator behavior: when a behavior implements
`IGrainUserServiceProviderFactory`, the generator emits an `AddGrainUserServiceProviderFactory<,>` call;
and that `IActivationMemory<T>`/`IManagedActivationMemory<T>`/`IEagerActivationMemory<T>` accessor
registrations now go through `AddQuarkOwnedScoped<T>` instead of plain `AddScoped<T>` (functionally
identical for behaviors that don't opt in — this is what makes the Quark-only satellite provider possible
for those that do). Note the v1 limitation: `IPersistentActivationMemory<T>`/`[PersistentState]` are not
yet supported for opted-in behaviors.

- [ ] **Step 4: Final full-repo verification**

Run: `dotnet build Quark.slnx && dotnet test Quark.slnx`
Expected: Both succeed cleanly.

- [ ] **Step 5: Commit**

```bash
git add FEATURES.md wiki/Source-Generators.md
git commit -m "Document IGrainUserServiceProviderFactory in FEATURES.md and Source-Generators wiki"
```
