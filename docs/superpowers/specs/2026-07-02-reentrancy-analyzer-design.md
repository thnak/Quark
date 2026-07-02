# Design: Reentrancy-deadlock analyzer
**Issue:** #117
**Date:** 2026-07-02
**Status:** Draft — ready for implementation

---

## 1. Problem statement

A non-reentrant grain A that awaits a call which transitively re-enters A deadlocks: A's
mailbox turn is blocked waiting on the inner call, and the inner call can never get a turn
on A's serialized mailbox. Quark surfaces this today only at runtime, *after* it happens,
via `StuckGrainDetector` (post-hoc, threshold-based). There is no dev-time signal. The issue
asks for a Roslyn analyzer that flags the pattern at compile time, following the existing
`Quark.Analyzers` numbering scheme, and explicitly acknowledges that full cross-assembly
call-graph cycle detection is infeasible in a single-project analyzer — so it requests a
*narrow heuristic*.

## 2. Quark reentrancy model (as verified against source)

Verified in `src/Quark.Runtime/GrainActivation.cs`, `LocalGrainCallInvoker.cs`, and
`src/Quark.Core.Abstractions/Grains/ReentrantAttribute.cs`:

- **`[ReentrantAttribute]`** (`Quark.Core.Abstractions.Grains`, `AttributeTargets.Class`,
  `Inherited = true`) is the only reentrancy control. It is read at activation time in
  `LocalGrainCallInvoker.CreateActivationAsync`:
  `bool isReentrant = behaviorType.IsDefined(typeof(ReentrantAttribute), inherit: true);`
  and passed into the `GrainActivation` ctor.
- **Mailbox serialization** lives in `GrainActivation.PostAsync`:
  ```
  if (_isReentrant) { ...; return workItem(); }   // bypass queue, run immediately
  return PostCoreAsync(workItem);                  // else enqueue on Channel<MailboxWorkItem>
  ```
  Non-reentrant grains run one work item at a time on a serial mailbox loop. A second work
  item posted while the first is still awaiting sits in the queue until the first completes.
- **There is NO same-grain / same-call-chain short-circuit.** `PostAsync` branches on
  `_isReentrant` only — there is no request-context / call-chain tracking (unlike Orleans,
  which lets calls in the same chain re-enter). Therefore a non-reentrant grain that awaits
  a call routed back to its own activation **deadlocks unconditionally**. This is the
  definite case worth flagging.
- **Grain-to-grain calls from a behavior:** a behavior injects `IGrainFactory`, calls
  `GetGrain<ISomeGrain>(key)` to get a generated proxy, and awaits a proxy method. The proxy
  routes through `IGrainCallInvoker.InvokeAsync` → `GetOrActivateAsync` → the target
  activation's `PostAsync`. If the target activation is the *same* activation the caller is
  running on and that activation is non-reentrant, the awaited `PostCoreAsync` never drains →
  deadlock. Local same-grain calls are **not** detected or short-circuited today.
- **Behaviors implement their own grain interface.** Confirmed across samples
  (`PlayerBehavior : IGrainBehavior, IPlayerGrain`, `AccountBehavior : IGrainBehavior,
  IAccountGrain`, etc.). `invokable.Invoke(behavior)` invokes through that interface. This is
  the static anchor for the self-call heuristic: the class advertises exactly which interface
  is "its own".
- **`GrainTimerCreationOptions.Interleave`** (`src/Quark.Core.Abstractions/Timers/`) is a
  *separate* interleaving axis (a timer callback may run while a prior callback is still
  executing). It is **out of scope** for this analyzer: it governs timer-vs-timer overlap,
  not grain-call re-entrancy, and a `false` value is the safe default. Noted here only so the
  implementer does not conflate the two knobs.

**Runtime complement:** `StuckGrainDetector` (`Quark.Diagnostics`, emits `OnMailboxStuck`
past `DiagnosticOptions.StuckThreshold`) remains the catch-all for the deadlocks this static
rule cannot prove. The analyzer is the cheap first line; the detector is the backstop.

## 3. Goals / Non-goals

**Goals**
- Ship a low-false-positive, dev-time signal for the two highest-confidence reentrancy
  deadlock shapes, mirroring the QRK0030/QRK0031 analyzer + test structure.
- Zero new runtime dependencies; analyzer-only; AOT-irrelevant (runs at compile time).
- Honest, documented false-negative surface — the analyzer must not imply completeness.

**Non-goals**
- Full cross-assembly / cross-project call-graph cycle detection. **Infeasible and out of
  scope**, stated honestly: a `DiagnosticAnalyzer` sees one `Compilation` at a time. Grain B
  frequently lives in a different assembly (referenced only as an interface), so a B→A edge
  is invisible; even within one compilation, proving that a transitive edge re-enters the
  *same activation* requires proving grain-key equality across method boundaries, which
  Roslyn dataflow cannot do reliably. Attempting it produces either near-zero coverage or
  heavy false positives.
- Flagging legitimate cross-instance calls to the *same grain type but different key*
  (e.g. a `ChatUser` grain messaging another `ChatUser`). That is a normal, non-deadlocking
  pattern and must not warn.
- No code fixer in v1 (the safe fix — add `[Reentrant]`, restructure the call, or make it
  fire-and-forget — is a human judgment call, unlike the mechanical QRK0031 fix).

## 4. Diagnostic design

Two sibling rules in a new `Quark.Reentrancy` category. Rule A is the reliable primary
deliverable; Rule B is the issue's requested self-call heuristic, tiered by confidence.

> **ID ALLOCATION — reconciled 2026-07-02.** IDs in use today (grepped across `src/`,
> `tests/`, `docs/`): **QRK0001–0004, 0010–0012, 0020–0023, 0030–0031**. The sibling spec
> `2026-07-02-replay-determinism-analyzer-design.md` (same analyzer project, drafted the same
> day) has claimed **QRK0040** (`Quark.Determinism`). This spec therefore claims
> **QRK0041 (sync-over-async)** and **QRK0042 (self-reentrant call)** — the numbering below is
> final unless `src/Quark.Analyzers/AnalyzerReleases.Unshipped.md` shows otherwise at
> implementation time. Do not ship two analyzers sharing an ID.

### Rule A — QRK0041: Synchronous blocking on a grain call inside a grain behavior
- **Category:** `Quark.Reentrancy` · **Severity:** `Warning` · enabled by default.
- **Rationale:** blocking on an async grain call with `.Result` / `.Wait()` /
  `.GetAwaiter().GetResult()` from inside a behavior method occupies the mailbox turn
  synchronously while the inner call needs a turn to complete. This is a classic
  sync-over-async deadlock and is a bug regardless of which grain is targeted or whether it
  is reentrant. **Highest confidence, effectively zero false positives** — this is the rule
  that earns its keep.
- **Trigger:** inside a method (or lambda/local function) of a class implementing
  `Quark.Core.Abstractions.Grains.IGrainBehavior`, a member access / invocation of
  `Task.Result`, `Task<T>.Result`, `ValueTask.Result`, `ValueTask<T>.Result`,
  `Task.Wait(...)`, `.GetAwaiter().GetResult()`, or `.GetResult()` **whose receiver
  expression is the result of a grain-proxy call** — i.e. the innermost invocation's target
  method is declared on an interface deriving from `Quark.Core.Abstractions.IGrain`.
  - Scoping to grain-proxy receivers (rather than "any awaitable") keeps false positives at
    zero; blocking on a plain `Task.Delay` is bad style but not a mailbox deadlock. Reported
    as a resolved decision below; broadening is a documented future option.
- **Message:** `Blocking on grain call '{0}' with '{1}' inside grain behavior '{2}' occupies
  the mailbox turn and can deadlock; await the call instead.`

### Rule B — QRK0042: Non-reentrant behavior calls back into its own grain interface
- **Category:** `Quark.Reentrancy` · **Severity:** `Warning` (provable-self) — see tiering.
- **Rationale:** the issue's requested narrow heuristic. The behavior's own grain interface
  is statically known (the class implements it alongside `IGrainBehavior`). An awaited call
  on a proxy typed as that same interface *may* re-enter the same activation.
- **The honesty problem, and how it is handled by tiering the trigger:**
  A same-interface call only deadlocks when it targets the **same key** (same activation).
  Roslyn cannot in general prove key equality, and same-type/different-key calls are
  legitimate. So a blanket "any call on own interface" warning would be noisy and wrong.
  Two tiers:
  - **Tier 1 (provable self, Warning):** the awaited receiver is provably the current grain.
    Recognized shapes: `factory.GetGrain<IOwn>(<selfKeyExpr>)` where `IOwn` is the behavior's
    own grain interface **and** `<selfKeyExpr>` is the grain's own identity key — i.e. it
    reads from the injected `ICallContext` (per project memory, `ICallContext` is the live
    per-call identity API; `IGrainContext` is dead), e.g. `_ctx.GrainId.Key` /
    `ctx.GrainId.Key`. This is a definite self-deadlock. Warning.
  - **Tier 2 (heuristic, Info, opt-in escalation):** any awaited invocation whose target
    method is declared on the behavior's own grain interface `IOwn`, where the class is
    **not** `[Reentrant]`, and the receiver is **not** provably a different key. Reported at
    `Info` (like QRK0031) to acknowledge the false-positive surface (different-key calls).
    Suppressible per-line; escalating the project's `Quark.Reentrancy` severity turns it into
    a warning for teams that want it strict.
- **Guard:** suppress both tiers entirely if the class carries `[Reentrant]` (reentrant
  grains bypass the queue — no deadlock).
- **Message (Tier 1):** `Grain behavior '{0}' awaits a call on its own grain '{1}' for the
  same identity; a non-reentrant grain cannot re-enter itself and will deadlock. Mark the
  grain [Reentrant] or restructure the call.`
- **Message (Tier 2):** `Grain behavior '{0}' calls method '{1}' on its own grain interface
  '{2}'. If this targets the same key, a non-reentrant grain will deadlock. Verify the key
  differs or mark the grain [Reentrant].`

### Honest limits / false-negative disclosure (must appear in the rule help/docs)
- **No transitive detection.** A→B→A and any longer cycle is undetected (cross-assembly
  invisibility; no cross-method key-equality proof). Deferred to `StuckGrainDetector` at
  runtime. Documented as an explicit non-goal.
- **Tier-2 key-blindness.** Same-type/different-key calls cannot be distinguished from
  same-key self-calls in the general case; hence Tier 2 is `Info`, not `Warning`.
- **Indirection blindness.** Storing a proxy in a field/variable and awaiting it later, or
  reaching the proxy through a helper method, defeats the syntactic match.
- **Reflection / dynamic dispatch** grain calls are invisible (and separately flagged by the
  AOT analyzers QRK0001/0004).

## 5. Test plan

Mirror `ValueTaskPerformanceAnalyzerTests` — add
`tests/Quark.Tests.CodeGenerator/ReentrancyAnalyzerTests.cs` driven by the existing
`AnalyzerTestDriver`. Cases:

**QRK0041 (sync-over-async)**
- `.Result` on a grain-proxy call inside a behavior method → 1 diagnostic. (fires)
- `.Wait()` on a grain-proxy call → fires.
- `.GetAwaiter().GetResult()` on a grain-proxy call → fires.
- `await` on the same grain call → no diagnostic. (must not fire)
- `.Result` on a non-grain `Task` (e.g. `Task.FromResult(1).Result`) inside a behavior → no
  diagnostic (scoping guard).
- `.Result` on a grain call in a **non-behavior** class → no diagnostic.
- Block-bodied and expression-bodied method forms both covered (parallels the QRK0030/0031
  block-vs-scoped coverage in commit `cdae40e`).

**QRK0042 Tier 1 (provable self)**
- `factory.GetGrain<IOwn>(_ctx.GrainId.Key)` awaited in non-reentrant behavior → Warning.
- Same, but class is `[Reentrant]` → no diagnostic.
- `factory.GetGrain<IOther>(_ctx.GrainId.Key)` (different interface) → no diagnostic.

**QRK0042 Tier 2 (heuristic)**
- Awaited call on own interface with a literal/other key → Info.
- Awaited call on own interface where receiver key is a *different* grain's key expression →
  still Info (documented FP) — assert Info to lock current behavior, with a comment noting it
  is a known false positive.
- Class `[Reentrant]` → no diagnostic.
- Call on a *different* grain interface → no diagnostic.

**Meta**
- `AnalyzerReleases.Unshipped.md` updated; a test (or the analyzer-release tracking build)
  confirms declared IDs match descriptors (guards the collision-renumber path).

## 6. Implementation checklist (ordered, no circular deps)

1. `src/Quark.Analyzers/ReentrancyAnalyzer.cs` — new `[DiagnosticAnalyzer]` exposing
   `QRK0041` + `QRK0042` descriptors. Register `SyntaxNodeAction` on
   `SimpleMemberAccessExpression` (Rule A blocking members) and `InvocationExpression`
   (Rule A `.Wait()`/`.GetResult()` and Rule B proxy calls). Reuse the enclosing-behavior
   detection pattern from `ValueTaskPerformanceAnalyzer` (`AllInterfaces` scan for
   `IGrainBehavior`) and its `IsInside…` scope-walk helper (stop at
   lambda/local-function boundaries). Add helpers: `IsGrainProxyCall(invocation)` (target
   method's containing type derives from `IGrain`), `GetOwnGrainInterface(behaviorType)`,
   `IsReentrant(behaviorType)`, `IsSelfKeyExpression(expr)` (matches `*.GrainId.Key` off an
   `ICallContext`-typed symbol).
2. `src/Quark.Analyzers/AnalyzerReleases.Unshipped.md` — append `QRK0041` (Warning) and
   `QRK0042` (Info) rows under `Quark.Reentrancy`. **First re-confirm the IDs are free**
   (collision note in §4).
3. `tests/Quark.Tests.CodeGenerator/ReentrancyAnalyzerTests.cs` — per §5.
4. `wiki/` (e.g. a short "Reentrancy analyzer" note in the analyzers/AOT page or
   `FEATURES.md`) — document both rules, their triggers, and the explicit false-negative
   disclosure so users do not treat green as "deadlock-free".
5. Run `dotnet test tests/Quark.Tests.CodeGenerator/Quark.Tests.CodeGenerator.csproj`.

No new project references, no runtime changes, no generator changes. `Quark.Analyzers`
already ships in the analyzer package; adding a class needs no wiring.

## 7. Resolved design decisions

- **Two rules, not one.** Sync-over-async (QRK0041) and self-reentrant call (QRK0042) are
  distinct shapes with distinct confidence and distinct suppression stories; separate IDs let
  users tune them independently. Matches the QRK0030/0031 split precedent.
- **QRK0041 scoped to grain-proxy receivers**, not all awaitables — zero false positives now;
  broadening to any blocked `Task` in a behavior is a documented future toggle, not v1.
- **QRK0042 tiered (Warning provable-self / Info heuristic)** rather than a single blanket
  warning — honors the issue's requested heuristic while refusing to warn on the legitimate
  same-type/different-key pattern. Severity `Info` for Tier 2 mirrors QRK0031's conservative
  stance.
- **Cross-compilation A→B→A cycle detection: CUT.** Infeasible in a single-`Compilation`
  analyzer and FP-prone even where visible; `StuckGrainDetector` is the runtime backstop.
- **No code fixer in v1** — the correct remediation is a design choice, not a mechanical edit.
- **Compatibility tier: Quark-native.** Orleans ships no equivalent analyzer; this is a new
  diagnostic surface with no Orleans API to match. (The runtime concept it guards,
  `[Reentrant]`, is drop-in.)

## 8. Dependencies & related work

- **Depends on nothing new.** Reads only public abstractions already in
  `Quark.Core.Abstractions` (`IGrainBehavior`, `IGrain`, `ReentrantAttribute`,
  `ICallContext`).
- **Runtime complement:** `StuckGrainDetector` / `AddQuarkStuckGrainDetector()`
  (`Quark.Diagnostics`) — the post-hoc detector for everything this static rule cannot prove.
  Docs for both rules should cross-link to it as the backstop.
- **Coordinate with:** `docs/superpowers/specs/2026-07-02-replay-determinism-analyzer-design.md`
  (parallel, same analyzer project, same ID block — see the collision warning in §4). Not yet
  on disk at time of writing; the implementer of whichever lands second must renumber if
  0040/0041 are taken.
- **Precedent to mirror:** `ValueTaskPerformanceAnalyzer.cs` +
  `ValueTaskPerformanceAnalyzerTests.cs` (commits `e637a07`, `cdae40e`) — same structure,
  category-per-concern, `AnalyzerReleases.Unshipped.md` discipline, block-vs-scoped test
  coverage.

## 9. Open questions

- **QRK0042 Tier 2 severity:** ship at `Info` (recommended, conservative) or `Warning`
  (louder, more false positives)? Recommendation: `Info`.
- **`ICallContext` self-key shape:** confirm the exact member path used to read the current
  grain's key from `ICallContext` in current behaviors (spec assumes `ctx.GrainId.Key`) so
  Tier 1's `IsSelfKeyExpression` matcher targets the real API.
- **Should QRK0041 also flag blocking on *any* awaitable in a behavior** (not just grain
  calls) as a separate low-severity lint? Deferred; recommend no for v1.
