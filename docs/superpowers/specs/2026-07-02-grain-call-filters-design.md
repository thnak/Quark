**Issue:** #36 — grain call filters (IIncomingGrainCallFilter / IOutgoingGrainCallFilter); supersedes #98  
**Date:** 2026-07-02  
**Status:** Planned — design also posted as a comment on the issue

## Grain call filters — final design

Supersedes the original draft. Verified against live source on `main`
(`LocalGrainCallInvoker`, `GrainProxyGenerator`, `GrainScopeBinder`,
`RequestContext`, `IGrainCallInvoker`). Two substantive corrections to the draft:
the `IGrainCallInvoker` surface is now **`ValueTask`-returning**, and the
outgoing pipeline is implemented as a **factory-boundary invoker decorator**,
not proxy codegen (rationale in *Resolved design decisions*).

### Goals / Non-goals

**Goals**
- Orleans-parity interception primitives: `IIncomingGrainCallFilter` (silo, runs on the grain's turn) and `IOutgoingGrainCallFilter` (caller side, before the hop).
- A single `IGrainCallContext` seam usable by both sides for auth, logging, retry, metrics, exception mapping, and result mutation.
- Global registration via DI plus zero-registration per-grain-type participation (a behavior may implement `IIncomingGrainCallFilter`).
- No-filter fast path stays allocation-free and codegen-free; filters are an opt-in slow path.
- Fully AOT/trim safe: no reflection, no scanning, explicit DI, generics stay linker-visible.

**Non-goals (v1)**
- No boxed `object?[] Arguments` on the context — exposing arguments would defeat the zero-alloc typed-invokable design. Deferred; revisit only with a source-gen accessor.
- No filtering of **observer** calls (`InvokeObserverAsync`) — fire-and-forget fan-out stays on the fast path.
- No ordering attributes / priorities in v1 — order is DI registration order.
- No new wire-protocol fields; `IGrainCallContext.RequestContext` reuses the existing ambient `RequestContext`.

### Proposed API

`Quark.Core.Abstractions/Hosting/` (abstractions only — no impl):

```csharp
public interface IGrainCallContext
{
    GrainId GrainId { get; }                                   // .Type is available for auth/routing
    uint MethodId { get; }                                     // stable numeric id from the invokable
    object? Result { get; set; }                               // boxed; null for void calls and outgoing-before-hop
    IGrainBehavior? Behavior { get; }                          // resolved behavior on incoming; null on outgoing
    IReadOnlyDictionary<string, object?> RequestContext { get; } // snapshot of the ambient RequestContext
    Task InvokeAsync();                                        // run the next filter, then the target
}

public interface IIncomingGrainCallFilter { Task InvokeAsync(IGrainCallContext context); }  // silo, in the activation turn
public interface IOutgoingGrainCallFilter { Task InvokeAsync(IGrainCallContext context); }  // caller side, before dispatch
```

Registration extensions (filters are **singletons** — all per-call state arrives via `IGrainCallContext`):

```csharp
// Quark.Runtime.RuntimeServiceCollectionExtensions
public static IServiceCollection AddIncomingGrainCallFilter<TFilter>(this IServiceCollection services)
    where TFilter : class, IIncomingGrainCallFilter;
public static IServiceCollection AddIncomingGrainCallFilter(this IServiceCollection services, IIncomingGrainCallFilter instance);

// Quark.Client.ClientServiceCollectionExtensions
public static IServiceCollection AddOutgoingGrainCallFilter<TFilter>(this IServiceCollection services)
    where TFilter : class, IOutgoingGrainCallFilter;
public static IServiceCollection AddOutgoingGrainCallFilter(this IServiceCollection services, IOutgoingGrainCallFilter instance);
```

**Compatibility tier:** drop-in for the interface names/shape (`IIncomingGrainCallFilter`, `IOutgoingGrainCallFilter`, `IGrainCallContext`, `Result`, `InvokeAsync()`); **minor-change** for DI (`AddIncomingGrainCallFilter<T>()` instead of Orleans' `AddIncomingGrainCallFilter(...)` overloads + convention wiring). `IGrainCallContext` intentionally omits Orleans' `Arguments`/`Grain`/`InterfaceMethod` reflection surface (AOT).

### Runtime integration (real anchors)

**Incoming — `src/Quark.Runtime/LocalGrainCallInvoker.cs`.**
Filters run inside the existing per-call turn, so they see the scoped services, the resolved behavior, and the ambient `RequestContext`. Integrate at the leaf of both `InvokeAsync<TInvokable,TResult>` (line ~93) and `InvokeVoidAsync<TInvokable>` (line ~148), inside the `activation.PostAsync(async () => { ... })` closure, immediately after `GrainScopeBinder.BindAndResolveAsync` returns the `behavior`:
- Fast path (unchanged): `if (!_hasIncomingFilters && behavior is not IIncomingGrainCallFilter) { r = await invokable.Invoke(behavior); ... }` — no context, no closure, no allocation.
- Slow path: build a `GrainCallContext` (new file `src/Quark.Runtime/GrainCallContext.cs`) seeded with `grainId`, `invokable.MethodId`, `behavior`, and `RequestContext.GetAll()`; its leaf runs `context.Result = await invokable.Invoke(behavior)` (boxed once), then after the chain `result = (TResult)context.Result!`. For void, the leaf awaits `invokable.Invoke(behavior)` and leaves `Result` null.
- Chain order: global singletons (registration order, outermost first) → the behavior itself if it implements `IIncomingGrainCallFilter` (innermost, runs last before the method) → leaf. This gives per-grain-type participation with zero registration.
- `_hasIncomingFilters` is computed once in the ctor from an injected `IEnumerable<IIncomingGrainCallFilter>` materialized to an array (the invoker is a singleton — one allocation at startup, none per call). The manual ctor wiring at `RuntimeServiceCollectionExtensions.cs:69` gains `filters: sp.GetServices<IIncomingGrainCallFilter>()`.
- Because integration sits *inside the local execution path* (after the `TryRouteRemote` short-circuit at line 78/134), incoming filters run exactly once, on the owning silo, for **both** the TCP-dispatch path (`MessageDispatcher` → `*_TransportDispatcher.DispatchAsync` → `invoker.InvokeAsync`) and the grain-to-grain path. No dispatcher changes needed.

**Outgoing — factory-boundary decorator (no codegen).**
New `src/Quark.Client/OutgoingFilterCallInvoker.cs` implements `IGrainCallInvoker` and wraps the real invoker. It intercepts `InvokeAsync<TInvokable,TResult>` and `InvokeVoidAsync<TInvokable>` (builds a `GrainCallContext` with `MethodId = invokable.MethodId`, `Behavior = null`, leaf = call inner, box result on return); `InvokeObserverAsync` forwards untouched. It resolves `IReadOnlyList<IOutgoingGrainCallFilter>` once; when empty it forwards directly (no context, no alloc).
- It is injected at the **grain-factory** boundary only — `LocalGrainFactory` (`src/Quark.Client/LocalGrainFactory.cs`) and `TcpGatewayGrainFactory` (`src/Quark.Client.Tcp/TcpGatewayGrainFactory.cs`) gain an additive optional ctor param `OutgoingFilterCallInvoker? outgoingInvoker = null` and pass it (when present) to `CreateProxy(...)` instead of the raw `_invoker`. Generated proxies, `IGrainProxyActivator`, and the proxy registries are **untouched**.
- The silo's `IGrainCallInvoker` DI registration (`RuntimeServiceCollectionExtensions.cs:82`) and `MessageDispatcher`'s injected invoker (`MessageDispatcher.cs`) stay the raw `LocalGrainCallInvoker`, so inbound TCP dispatch never runs outgoing filters even though it shares the invoker.
- `AddOutgoingGrainCallFilter<T>()` registers the filter as a singleton and idempotently `TryAddSingleton<OutgoingFilterCallInvoker>` (wrapping `sp.GetRequiredService<IGrainCallInvoker>()`). On a co-hosted host both extension sets apply cleanly.

**RequestContext bridge — `src/Quark.Core.Abstractions/Messaging/RequestContext.cs`.**
`IGrainCallContext.RequestContext` returns `RequestContext.GetAll()` (type is `IReadOnlyDictionary<string, object?>` — the draft's `<string,string>` was wrong). Filters that need to *write* ambient values call the existing static `RequestContext.Set(...)`, which already propagates down the call chain; the context property is a read snapshot.

**Diagnostics ordering.** `OnInvocationStart` (line 85/141) fires before the chain is built; `OnInvocationEnd` and the `GrainInvocations`/`GrainInvocationDuration` metrics (lines 106-109/156-159) fire after it completes — so filter time is counted inside invocation duration, and a filter short-circuit or throw is recorded as a normal completion/error.

### AOT & performance notes

- No reflection anywhere. The leaf uses the existing typed `IGrainInvokable<TResult>` struct, so generics stay linker-visible; the decorator is generic over the same `TInvokable`/`TResult`.
- Invokable structs are plain `readonly struct` (see `GrainProxyGenerator.EmitInvokableStruct` — `internal readonly struct …`), **not** `ref struct`, so capturing them in the leaf closure is legal. The draft's "must not capture ref struct invokables" concern was a misread — the ref structs are `CodecWriter`/`CodecReader`, which never enter the filter path.
- Filters defeat the zero-alloc path: one `GrainCallContext` + one leaf closure per filtered call, plus one box of the `TResult` (skipped for void). Documented as the explicit cost of turning filters on.
- No-filter fast path: incoming guarded by `_hasIncomingFilters` (a cached bool) plus a branch-only `behavior is IIncomingGrainCallFilter` check; outgoing decorator is only inserted when `AddOutgoingGrainCallFilter` was called, and even then forwards directly when the list is empty.
- Filter lifetime is singleton, so the filter list materializes once at startup — zero per-call enumerator/array allocation.

### Test plan

Unit (`tests/Quark.Tests.Unit`, hand-wired invoker/proxy per house style):
- [ ] Incoming ordering: two filters observe outermost-first / innermost-last around the method.
- [ ] Incoming short-circuit: a filter that returns without calling `InvokeAsync()` skips the method; result comes from `context.Result`.
- [ ] Result mutation: filter overwrites `context.Result`; caller observes the new value; wrong-type overwrite surfaces `InvalidCastException`.
- [ ] Exception mapping: filter wraps a method exception into a mapped type.
- [ ] Per-grain-type: a behavior implementing `IIncomingGrainCallFilter` runs last, with no DI registration.
- [ ] Void method: `Result` stays null; `InvokeVoidAsync` path filtered correctly.
- [ ] Fast-path guard: no filters registered ⇒ behavior resolved and invoked with no `GrainCallContext` allocation (assert via a counting behavior / no observable context side effects).
- [ ] Outgoing ordering + short-circuit via `OutgoingFilterCallInvoker` over a fake inner `IGrainCallInvoker`; `InvokeObserverAsync` bypasses filters.

Integration (`tests/Quark.Tests.Integration`, `TestCluster`):
- [ ] Incoming filter runs once per call over the real TCP dispatch path and the grain-to-grain path; not double-invoked.
- [ ] Outgoing filter (client) mutates `RequestContext` and the value is observable by an incoming filter on the silo.
- [ ] Inbound TCP dispatch does **not** trigger outgoing filters (regression guard for the shared-invoker separation).

AOT:
- [ ] Native AOT smoke publish of `Quark.Runtime` with an incoming filter registered — no trim/AOT warnings.

### Implementation checklist

- [ ] Add `IGrainCallContext`, `IIncomingGrainCallFilter`, `IOutgoingGrainCallFilter` to `src/Quark.Core.Abstractions/Hosting/`.
- [ ] Add `src/Quark.Runtime/GrainCallContext.cs`: non-generic context + array/index chain runner (`InvokeAsync()` advances to next filter or leaf).
- [ ] Wire incoming filters into `LocalGrainCallInvoker` (`InvokeAsync` + `InvokeVoidAsync`): ctor `IEnumerable<IIncomingGrainCallFilter>` param, cached `_hasIncomingFilters`, fast-path guard incl. `behavior is IIncomingGrainCallFilter`, slow-path chain at the leaf; preserve diagnostics/metrics ordering.
- [ ] Update the manual `LocalGrainCallInvoker` registration at `RuntimeServiceCollectionExtensions.cs:69` to pass `sp.GetServices<IIncomingGrainCallFilter>()`.
- [ ] Add `AddIncomingGrainCallFilter<T>()` / instance overload to `RuntimeServiceCollectionExtensions`.
- [ ] Add `src/Quark.Client/OutgoingFilterCallInvoker.cs` (decorator; empty-list forward; observer pass-through).
- [ ] Add `AddOutgoingGrainCallFilter<T>()` / instance overload + idempotent `TryAddSingleton<OutgoingFilterCallInvoker>` to `ClientServiceCollectionExtensions`.
- [ ] Additive optional ctor param `OutgoingFilterCallInvoker? outgoingInvoker = null` on `LocalGrainFactory` and `TcpGatewayGrainFactory`; prefer it over the raw invoker when non-null.
- [ ] Tests per plan; AOT smoke build.
- [ ] Docs: new `wiki/Grain-Call-Filters.md` (or a section in `wiki/Architecture.md`) + `FEATURES.md` parity row; note the boxing cost and the "filters modify, listeners observe" boundary.

### Resolved design decisions

- **Global vs per-grain-type registration (#98):** support **both** — global filters via `AddIncomingGrainCallFilter<T>()` (DI singleton), per-grain-type by having the behavior implement `IIncomingGrainCallFilter` (runs innermost, zero registration). *Rationale: matches Orleans exactly and needs no reflection or scanning.*
- **Filters vs `IQuarkDiagnosticListener` (#98):** keep them separate — filters are a **behavior-modifying** pipeline (may short-circuit, mutate `Result`, throw); the diagnostic listener is **pure observability** (`in`-ref-struct events, no-op default, no control flow). *Rationale: mixing control flow into the observability sink would break its allocation-free, side-effect-free contract.*
- **Outgoing implementation: decorator, not proxy codegen (#36):** implement outgoing filters as an `IGrainCallInvoker` decorator injected at the grain-factory boundary. *Rationale: proxy codegen would force a breaking `IGrainProxyActivator.Create` signature change across every generated + hand-written proxy and bloat the fast path, and the silo shares one `IGrainCallInvoker` between inbound dispatch and outbound calls — a DI-level decorator would wrongly filter inbound TCP calls; the factory seam scopes filtering to genuine outbound calls only.*
- **Filter lifetime:** singleton; all per-call state flows through `IGrainCallContext`. *Rationale: keeps the filter list a one-time startup allocation and avoids scoped-into-singleton capture bugs.*
- **`RequestContext` element type:** `IReadOnlyDictionary<string, object?>` (not `<string,string>`). *Rationale: mirrors the actual `RequestContext.GetAll()` signature.*
- **Ordering/short-circuit:** DI registration order, outermost-first; a filter that skips `InvokeAsync()` short-circuits the rest of the chain and the method, and its `context.Result` is returned. *Rationale: standard middleware semantics, matches Orleans.*
- **`Arguments` on the context:** omitted in v1. *Rationale: a boxed `object?[]` would defeat the zero-alloc typed-invokable path; add later via source-gen if demanded.*
- **Observer calls:** not filtered by either pipeline. *Rationale: fire-and-forget fan-out must stay on the fast path; filtering there has no Orleans precedent worth the cost.*
- **Result boxing:** accepted only when filters are present (skipped for void). *Rationale: explicit, opt-in slow-path cost documented at the API.*
