# Why Quark

Quark uses Orleans vocabulary — grains, silos, clients, placement, persistence, reminders, streams — on purpose. That raises a fair question: **if it looks like Orleans, why not just use Orleans?** And if you come from Akka.NET: **where is the supervision story, and why should I trust a young runtime?**

This page answers those questions honestly: what Quark bets on, what it costs, what is proven today versus still being hardened, and when you should pick Orleans or Akka.NET instead.

## The one-paragraph answer

Quark is a **Native AOT-first distributed actor engine**. The runtime — not your classes — owns activation identity, lifecycle, scheduling, state, resources, and cleanup. Your code plugs into those boundaries through explicit, source-generated, trim-safe APIs. Orleans vocabulary is the on-ramp; the engine contract is the identity. If you do not need Native AOT, per-call DI scoping, or a strictly analyzable runtime, Orleans is the more mature choice today — and this page will keep saying so until Quark's hardening roadmap ([Track A](https://github.com/thnak/Quark/issues/50), [B](https://github.com/thnak/Quark/issues/51), [C](https://github.com/thnak/Quark/issues/52), [D](https://github.com/thnak/Quark/issues/53)) says otherwise.

## "This is just Orleans with extra rules"

The vocabulary is shared; the runtime contract is not.

| | Orleans / Akka.NET | Quark |
|---|---|---|
| Actor object | Long-lived class instance; fields *are* the state | Long-lived **shell** (`GrainActivation`) owns state; the behavior object is rebuilt per call and discarded |
| State location | Instance fields, `Grain<TState>`, actor props | Engine-owned APIs: `IActivationMemory<T>`, `IPersistentActivationMemory<T>`, `IManagedActivationMemory<T>`, `[PersistentState]`, `JournaledGrain` |
| Dependency scope | Constructor injection at activation; scoped services need manual workarounds | Fresh `IServiceScope` per call — `DbContext`, tenant context, and other scoped services work the way ASP.NET Core taught you |
| Discovery | Reflection + assembly scanning (Orleans has generators too, but reflection fallbacks remain) | 100% source-generated: proxies, serializers, DI registration. No scanning, no runtime emit |
| Deployment target | JIT-first; AOT possible with caveats | Native AOT is the primary validated path — every package is `IsTrimmable` with AOT analyzers on |

The "extra rules" are the point: because behaviors are stateless-by-construction and every cross-call resource lives behind an engine API, the engine can guarantee cleanup, surface lifecycle in diagnostics, and validate the whole object graph at startup (`BehaviorStartupValidator`) instead of on the first 3 a.m. call.

Compatibility with Orleans is an **entry ramp, not the core story**. The [Orleans Migration Guide](Orleans-Migration) enumerates exactly which APIs are drop-in, which need DI rewiring, and which concepts are Quark-native.

## "Per-call behavior construction could be expensive"

It has a cost, and we would rather measure it than hand-wave it.

What actually happens per call:

- One `IServiceScope` + one behavior instance are created; the scope is disposed when the call completes.
- **Hot state is not rebuilt.** `IActivationMemory<T>` holders live on the shell and are handed to the behavior by reference — construction is field assignment, not state loading.
- Proxies, dispatch delegates, and codecs are generated at build time and cached; there is no per-call reflection.
- In-process calls never touch the serializer; data isolation between caller and grain is handled by generated deep-copiers, with analyzers (`QRK0010`–`QRK0012`) flagging exactly which arguments are shallow-cloned or boxed.

What we are doing about the remainder, in the open:

- [#65](https://github.com/thnak/Quark/issues/65) BenchmarkDotNet micro+macro suite with committed baselines
- [#66](https://github.com/thnak/Quark/issues/66) allocation reduction (pooled buffers, copy elimination)
- [#67](https://github.com/thnak/Quark/issues/67) hot-path profiling of scope/ExecutionContext/TCS costs
- [#68](https://github.com/thnak/Quark/issues/68)/[#69](https://github.com/thnak/Quark/issues/69) AOT cold-start footprint and vs-Orleans comparative benchmarks, published with methodology

Until those numbers are published, treat per-call scoping as "priced like an ASP.NET Core request scope": fine for I/O-bound grains (the overwhelming case), worth measuring for sub-microsecond hot loops. If a grain is truly compute-hot, `[StatelessWorker]` placement and shell-cached state keep the per-call cost to scope + constructor.

## "You are fighting normal C#"

Quark does change one habit: **behavior fields are per-call, not per-actor.** We do not rely on documentation to enforce that:

- `QRK0020` warns on any mutable instance field of a grain behavior ("will be reset between calls").
- `QRK0021` warns on writable auto-properties on behaviors.
- `BehaviorStartupValidator` fails silo startup on DI misconfiguration, so a missing state registration surfaces at boot, not mid-traffic.

So the wrong thing is not silently allowed — the compiler flags it as you type. The rule that remains is small and teachable:

> Constructor-injected, `readonly` fields (services, `IActivationMemory<T>` handles) — fine. Anything mutable that should outlive the call — put it in an engine-owned state API. See the decision table in [Persistence](Persistence) and the contract in [Lifecycle and Failure Semantics](Lifecycle-and-Failure-Semantics).

Mutable **static** state is the one hole the analyzers do not yet cover — tracked as [#129](https://github.com/thnak/Quark/issues/129) under the v1.0 epic [#128](https://github.com/thnak/Quark/issues/128).

## "AOT-first may over-constrain the framework"

AOT-first costs real conveniences. Being explicit about the trade:

| You give up | You get instead |
|---|---|
| Assembly-scanning discovery | Generated `AddMyAssemblyBehaviors()` — one line, build-time verified |
| Reflection-based serializers | `SerializerGenerator` from `[GenerateSerializer]`/`[Id]` — versionable, allocation-aware |
| Runtime proxy generation | `GrainProxyGenerator` at compile time |
| Convention-based registration | Explicit registration validated at startup |
| Plugin loading via `Assembly.Load` | Explicit provider registration (analyzer `QRK0002` will tell you) |

In exchange: native binaries with no JIT warm-up, small closed-world deployments, and — just as valuable on the JIT — a runtime whose entire object graph is visible to the compiler and analyzers, so "it worked in dev but the linker/reflection path broke in prod" is not a failure class.

Two things this does **not** mean:

1. **You are not forced to deploy AOT.** Everything runs on the JIT; AOT is the validated *option*, not a requirement. JIT-only fast paths are allowed in the codebase when guarded by `RuntimeFeature.IsDynamicCodeSupported`.
2. **Productivity ceremony is mostly generated away.** The generators exist precisely so that AOT-strictness does not translate into hand-written boilerplate — see [Source Generators](Source-Generators).

If your team values dynamic plugin ecosystems or reflection-heavy meta-programming above deployment characteristics, Orleans or Akka.NET will feel less rigid. That is a real trade-off, not a misunderstanding.

## "Distributed systems need maturity, not elegance"

Correct, and Quark does not claim otherwise. Status, honestly:

**What exists today**

- Dedicated fault-injection test projects (`Quark.Tests.Fault` unit-level, `Quark.Tests.Fault.Integration` over a real cluster) exercising storage errors and timeouts.
- An in-process `TestCluster` harness and Testcontainers-backed Redis integration tests.
- Multi-silo clustering with a distributed grain directory, TLS (incl. mutual TLS), and an idle-collection lifecycle.
- A first-class diagnostics surface (`IQuarkDiagnosticListener`) covering grain lifecycle, invocation, mailbox depth/stuck detection, connections, and observers — plus `StuckGrainDetector` for surfacing deadlocked grains.
- Native AOT publish as a smoke-tested path.

**What is openly not proven yet** — tracked as the pre-1.0 hardening program:

- [Track A — SAFE](https://github.com/thnak/Quark/issues/50): transport auth SPI, TLS hardening, fault isolation, wire-error information-disclosure review
- [Track B — TRUST](https://github.com/thnak/Quark/issues/51): delivery-guarantee semantics and dedup ([#59](https://github.com/thnak/Quark/issues/59)), failover hygiene ([#60](https://github.com/thnak/Quark/issues/60)), graceful drain ([#61](https://github.com/thnak/Quark/issues/61)), 2PC completion ([#62](https://github.com/thnak/Quark/issues/62)), reminder CAS + stream backpressure ([#63](https://github.com/thnak/Quark/issues/63)), property-based/partition/chaos/soak depth ([#64](https://github.com/thnak/Quark/issues/64))
- [Track C — FAST](https://github.com/thnak/Quark/issues/52): the benchmark program above
- [Track D — FOUNDATION](https://github.com/thnak/Quark/issues/53): CI gating (fault + integration + AOT publish + benchmark regression), SECURITY.md and a published delivery-guarantee table ([#71](https://github.com/thnak/Quark/issues/71))

If you need battle-tested-today reliability guarantees, choose Orleans. If you can adopt incrementally and want to influence how a stricter engine hardens, the roadmap above is where that happens.

## "Where is supervision?"

Quark's failure model is different from Akka.NET's parent-supervision tree, but it is defined — activation-scoped, engine-owned, and documented question by question (behavior throws, timers, queued calls, storage, caller error shape) in **[Lifecycle and Failure Semantics](Lifecycle-and-Failure-Semantics)**. The short version:

- A behavior exception is **contained to that call**: it propagates to the caller, the activation and its memory survive, and queued calls keep processing — the closest analogue is Akka's `Resume` directive, applied uniformly.
- Activation-fatal failures (activation hook failure, scope-initializer failure) remove the activation so the **next call gets a fresh one** — the virtual-actor equivalent of `Restart`.
- Escalation, cascading termination for parent/child grains, and poison-message quarantine are tracked openly ([#120](https://github.com/thnak/Quark/issues/120), Track B).

## "The state model may become fragmented"

There are several state APIs because there are several genuinely different lifetimes (per-call, per-activation, durable, event-sourced, external-resource). What keeps that from being a cognitive tax:

- One decision table with lifecycle diagrams, in [Persistence](Persistence) — including exactly what survives calls, deactivation, and restarts for each API.
- One lifecycle/cleanup contract in [Lifecycle and Failure Semantics](Lifecycle-and-Failure-Semantics).
- The default is simple: start with `IActivationMemory<T>`; add persistence only when the state must outlive the activation.

## "Per-call DI does not magically solve database performance"

Agreed — per-call scoping is a **correctness and isolation** feature (fresh `DbContext`, per-tenant scoped services via `AddGrainScopeInitializer`), not a database-performance feature. Connection pooling, batching, and idempotency remain your job, and the guidance lives in [Writing Grains § Data access from behaviors](Writing-Grains#data-access-from-behaviors): keep connections pooled and short-lived, batch inside a single grain call (the mailbox already serializes writers per key — a natural batching point), and treat the grain as the unit of write ownership rather than opening N contexts per logical operation.

## "Engine-owned lifecycle may reduce escape hatches"

The escape hatch is deliberate and first-class: `IManagedActivationMemory<T>` gives any long-lived resource an async factory, shell-cached lifetime, and guaranteed async cleanup on deactivation — warm caches, pooled clients, subscription handles, ring buffers, native handles:

```csharp
public MyBehavior(IManagedActivationMemory<RingBuffer> buffer)
{
    _buffer = buffer
        .Init(() => Task.FromResult(new RingBuffer(1024)))
        .Destroy(b => b.FlushAsync());
}
```

For work that is genuinely not actor-shaped (long background loops, batch pipelines), plain `IHostedService`s coexist with the silo in the same host — Quark does not ask you to force everything through grains. Timers, reminders, and streams cover the scheduled/reactive cases inside the model.

## When you should NOT choose Quark (yet)

Choose **Orleans** if you need years of production burn-in, the widest provider ecosystem, or reflection-friendly flexibility today. Choose **Akka.NET** if you need mature hierarchical supervision and actor-tree topologies. Choose **Quark** when several of these are true:

- Native AOT deployment (cold start, footprint, closed-world binaries) matters to you.
- You want scoped DI (`DbContext`, multi-tenancy) to work in actors without workarounds.
- You prefer build-time-verified explicitness over runtime convention magic.
- You can track a pre-1.0 runtime and want the failure/delivery semantics documented as contracts, not folklore.

The v1.0 exit criteria — documented contracts, tested failure semantics, cross-silo ownership, analyzer guardrails, published benchmarks — are public in [#128](https://github.com/thnak/Quark/issues/128).
