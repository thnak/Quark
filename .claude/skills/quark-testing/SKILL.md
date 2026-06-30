---
name: quark-testing
description: Use when writing tests for Quark grains — the in-process TestCluster/TestSilo/TestClient harness, configuring silo+client services for a test, and the manual (hand-wired) behavior/proxy/activation-memory registration used in test projects instead of the source generators. Quark-specific.
---

# Testing Quark Grains

## Overview

Use `Quark.Testing.Harness.TestCluster` for in-process integration tests — it runs a silo + client in-memory. Calls in this harness are **in-process (not serialized)**, so they don't exercise codecs; for TCP/serialization coverage use `Quark.Tests.Integration` (real gateway). In test projects, prefer **manual registration** (or hand-written proxies) over running the generators — simpler and avoids circular project references.

## Quick reference

| Need | Use |
|---|---|
| In-process cluster | `await using var c = await TestCluster.CreateAsync(opts => { ... });` |
| Configure silo DI | `opts.ConfigureSiloServices = services => { ... };` |
| Configure client DI | `opts.ConfigureClientServices = services => { ... };` |
| Get a grain | `c.Client.GetGrain<IFooGrain>("key")` |
| Per-test isolation | one cluster per test (`await using`) |

## TestCluster template

```csharp
public class FooGrainTests
{
    [Fact]
    public async Task Increment_accumulates()
    {
        await using var cluster = await TestCluster.CreateAsync(opts =>
        {
            opts.ConfigureSiloServices = services =>
            {
                // Generated path (if the Grains assembly references Quark.CodeGenerator):
                services.AddMyGrainsBehaviors();

                // OR manual path — register the behavior, its transport dispatcher,
                // and the activation-memory accessor explicitly:
                services.AddGrainBehavior<IFooGrain, FooBehavior>();
                services.AddScoped<IActivationMemory<FooState>>(sp =>
                    new ActivationMemoryAccessor<FooState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<FooState>()));
            };
            opts.ConfigureClientServices = services =>
                services.AddGrainProxy<IFooGrain, FooGrainProxy>();   // generated or hand-written proxy
        });

        var grain = cluster.Client.GetGrain<IFooGrain>("k");
        Assert.Equal(5, await grain.IncrementAsync(5));
        Assert.Equal(8, await grain.IncrementAsync(3));
    }
}
```

## Hand-written proxy (when not using the generator)

A proxy holds the `GrainId` + an `IGrainCallInvoker` and forwards each method by a numeric method id. See `tests/Quark.Tests.Unit/Integration/` and `samples/Realm/Realm.Tests/` for complete, compiling hand-written proxy + invokable examples to copy. Use `file`-scoped classes so test helpers stay out of the test assembly's public API.

## Persistence / storage in tests

Register the same providers the silo would: `services.AddInMemoryGrainStorage();` (and `AddSingleton<ILogStorage, InMemoryLogStorage>()` for journaled grains). For a persistence round-trip test, write state, force deactivation (e.g. via a behavior method that calls `shell.Deactivate(...)`, or activate a fresh grain id), then assert the reloaded value.

## Redis / external infra

Tests needing Redis use Testcontainers (`Quark.Tests.Integration`). Gate them so they skip when infra is unavailable: `[Trait("category","integration")]`.

## Running

```bash
dotnet test path/To/Tests.csproj
dotnet test path/To/Tests.csproj --filter "FullyQualifiedName~FooGrainTests"
```

## Common mistakes

- **Sharing one cluster across tests** → state bleed. Create one per test with `await using`.
- **Expecting serialization coverage from TestCluster** — in-process calls skip codecs. Add a `Quark.Tests.Integration` test for TCP/enum/DTO round-trips.
- **Forgetting the `IActivationMemory<T>` accessor** in the manual path → the behavior can't resolve its state.
- **Flaky timing tests under parallel load** — some timer/reminder tests pass in isolation but flake in parallel; verify with `--filter` before assuming a regression.

## Related skills

- quark-writing-grains — what you're testing
- quark-host-setup — the production wiring these tests mirror
