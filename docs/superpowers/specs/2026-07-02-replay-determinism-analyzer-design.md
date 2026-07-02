# Design: Deterministic-replay guard for JournaledGrain
**Issue:** #122
**Date:** 2026-07-02
**Status:** Draft — ready for implementation

## Problem statement

`JournaledGrain<TState, TEvent>.TransitionState(TState, TEvent)`
(`src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs:53`) is the single
function that applies an event to state. It runs in **two** places:

- Live path — `RaiseEvent(e)` calls `TransitionState(State, e)` immediately (line 60).
- Replay path — `OnActivateAsync` → `ReloadFromLogAsync` re-applies every confirmed event
  through `TransitionState` after a crash/reactivation (line ~100).

Its docstring says "Implement as a pure function," but that is a **convention only** —
nothing enforces it. If an author calls `Guid.NewGuid()`, `DateTime.UtcNow`, `new Random()`,
etc. inside `TransitionState` (or a same-type helper it calls), the state produced during
original execution silently diverges from the state produced on replay. There is no error,
just quietly wrong state — the canonical event-sourcing footgun that both Azure Durable
Functions (replay-safe `context.NewGuid()`) and DurableTask (dedicated nondeterminism
checking) build tooling around.

Quark already ships a Roslyn analyzer family (`Quark.Analyzers`) for exactly this class of
static footgun detection. This spec adds a determinism analyzer to that family and settles
the "safe helper" question the issue raises.

## Goals / Non-goals

### Goals
- Statically flag calls to a curated set of known-nondeterministic BCL APIs when they appear
  inside a `TransitionState` override, or inside any method **declared on the same type**
  that is (transitively) reachable from `TransitionState`.
- Reuse the established analyzer + test structure (mirror QRK0030/QRK0031: descriptor,
  `AnalyzerReleases.Unshipped.md` entry, `AnalyzerTestDriver`-based unit tests).
- Ship the **guidance** ("capture nondeterminism into the event at `RaiseEvent` time") as the
  primary, first-class remediation — documented in the diagnostic message and `HelpLinkUri`.
- Keep the analyzer dependency-free (matches the banned symbols by fully-qualified display
  string; requires no `Quark.Persistence.Abstractions` project reference).

### Non-goals
- **No inter-procedural / whole-program analysis.** Reachability stops at the type boundary
  (see "Honest limits"). This is a best-effort footgun catcher, not a soundness proof.
- **No new runtime API.** Explicitly rejecting an injected deterministic clock/id source
  (see "Fixer decision" and "Resolved design decisions"). The correct pattern needs zero
  runtime surface.
- **No automated code fixer** for the general case (justified below).
- Not attempting to catch nondeterminism from injected services, static utility classes,
  virtual dispatch, or captured delegates — out of scope for a syntactic analyzer.
- Not touching `JournaledGrain` itself, nor the Event Sourcing V2 work; this is analyzer-only.

## Diagnostic ID allocation

Full sweep of QRK IDs currently in use across **both** `src/Quark.Analyzers` (analyzers) and
`src/Quark.CodeGenerator` (generators also emit QRK diagnostics):

| Range | Category | Owner | Source |
|---|---|---|---|
| QRK0001–QRK0004 | `Quark.AOT` | `ReflectionUsageAnalyzer` | analyzer |
| QRK0010–QRK0012 | `Quark.DataIsolation` | `DataIsolationAnalyzer` | analyzer |
| QRK0020–QRK0021 | `Quark.BehaviorLifecycle` | `BehaviorStateAnalyzer` | analyzer |
| QRK0022–QRK0023 | `Quark.CodeGenerator` | `BehaviorRegistrationGenerator` | **generator** |
| QRK0030–QRK0031 | `Quark.Performance` | `ValueTaskPerformanceAnalyzer` | analyzer |

The codebase clusters each analyzer into its own decade. **Next free decade: QRK0040.**

Allocation for this feature:

| ID | Category | Severity | Meaning |
|---|---|---|---|
| **QRK0040** | `Quark.Determinism` | **Warning** | Nondeterministic API called in a `JournaledGrain` replay path (`TransitionState` or a same-type method reachable from it) |
| QRK0041 | `Quark.Determinism` | (reserved) | Reserved for a future companion rule (e.g. flagging nondeterminism in an event-sourced `OnActivateAsync` replay override). Do not use yet. |

Only **QRK0040** ships now. QRK0041 is reserved to keep the decade coherent.

**Severity rationale — Warning, not Error.** The AOT rules (QRK0001–0003) are Errors because
they are sound (a reflected type either is or isn't referenced). This analysis is a
best-effort heuristic with real false-negative surface (it cannot see across the type
boundary) and non-zero false-positive surface (an author may legitimately call `DateTime.UtcNow`
inside a helper that `TransitionState` never actually reaches on the replay-relevant branch).
Failing the build on a heuristic is too aggressive. Warning + `isEnabledByDefault: true`
matches QRK0030 and lets teams escalate to Error via `.editorconfig` if they want.

Register in `src/Quark.Analyzers/AnalyzerReleases.Unshipped.md`:

```
 QRK0040 | Quark.Determinism | Warning | Nondeterministic API in JournaledGrain replay path (TransitionState)
```

## Proposed API

No public API. One internal analyzer type and one diagnostic descriptor.

```csharp
namespace Quark.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeterministicReplayAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor NondeterministicApiInReplayPath; // QRK0040
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
    public override void Initialize(AnalysisContext context);
}
```

Descriptor shape (mirrors `ValueTaskPerformanceAnalyzer.TaskReturnOnBehavior`):

```csharp
public static readonly DiagnosticDescriptor NondeterministicApiInReplayPath = new(
    "QRK0040",
    "Nondeterministic API in a JournaledGrain replay path",
    "'{0}' is nondeterministic and runs during both live execution and replay in '{1}'. "
        + "Capture this value into the event at RaiseEvent time instead of generating it in TransitionState.",
    "Quark.Determinism",
    DiagnosticSeverity.Warning,
    isEnabledByDefault: true,
    description: "TransitionState (and same-type methods it calls) is re-executed on activation "
        + "to rebuild state from the log; nondeterministic values make replay diverge from the "
        + "original run. Generate the value once at RaiseEvent time and store it in the event.",
    helpLinkUri: "https://github.com/thnak/Quark/wiki/Persistence#deterministic-replay");
```

Message arg `{0}` = the offending API display (e.g. `Guid.NewGuid()`), `{1}` = the enclosing
`JournaledGrain` subtype name.

### Banned API set (v1)

Matched by containing-type FQN + member name so the exact overload/property is irrelevant:

| API | Node kind | Match |
|---|---|---|
| `Guid.NewGuid()` | invocation | `System.Guid` . `NewGuid` |
| `DateTime.Now` / `.UtcNow` / `.Today` | member access (property) | `System.DateTime` |
| `DateTimeOffset.Now` / `.UtcNow` | member access (property) | `System.DateTimeOffset` |
| `Environment.TickCount` / `.TickCount64` | member access (property) | `System.Environment` |
| `new Random()` (parameterless) | object creation | `System.Random` ctor, 0 args |
| `Random.Shared` | member access (property) | `System.Random` |
| `Stopwatch.GetTimestamp()` | invocation | `System.Diagnostics.Stopwatch` . `GetTimestamp` |

Notes: `new Random(seed)` (seeded) is **not** flagged — it is deterministic given the seed.
`DateTime.MinValue`/`MaxValue`/`UnixEpoch` are constants and never match (property match is
name-scoped to `Now`/`UtcNow`/`Today`). The set is a `const`-defined table so future additions
(e.g. `TimeProvider.System.GetUtcNow()`) are one-line edits.

## Analyzer design

### Strategy: symbol-anchored, syntax-walked, semantic-verified

Use a **symbol action on `SymbolKind.NamedType`** as the anchor (not a bare syntax-node
action) so we only do work for grains that actually derive from `JournaledGrain<,>`:

1. **Anchor.** `RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType)`. In it, walk
   `type.BaseType` upward; bail unless some base's `OriginalDefinition.ToDisplayString()`
   equals `Quark.Persistence.Abstractions.Journaling.JournaledGrain<TState, TEvent>`.
   (String match → no project reference to the persistence package needed.)

2. **Find the entry method.** Locate the `TransitionState` override on the type
   (`method.Name == "TransitionState" && method.IsOverride`, 2 params). If none, bail.

3. **Build the same-type reachable set.** Starting from `TransitionState`, compute the set of
   methods **declared on this type (all partial parts)** transitively invoked. Walk each
   method's syntax (`DeclaringSyntaxReferences`), collect `InvocationExpressionSyntax`, resolve
   each callee via the syntax tree's `SemanticModel`; if the callee's `ContainingType` is this
   type (or a partial part of it) and not yet visited, enqueue it. Worklist/BFS with a visited
   set keyed on `IMethodSymbol` (SymbolEqualityComparer). This gives an intra-type call graph
   rooted at `TransitionState`.

4. **Scan reachable bodies.** For every method in the reachable set, walk its descendant
   nodes and match the banned-API table:
   - `InvocationExpressionSyntax` → resolve `IMethodSymbol`, compare containing-type FQN +
     name (`Guid.NewGuid`, `Stopwatch.GetTimestamp`).
   - `MemberAccessExpressionSyntax` → resolve `IPropertySymbol`, compare containing-type FQN +
     name (`DateTime.UtcNow`, `Random.Shared`, `Environment.TickCount64`, …).
   - `ObjectCreationExpressionSyntax` → resolve ctor `IMethodSymbol`, check
     `ContainingType` is `System.Random` and `Parameters.Length == 0`.
   Report **QRK0040 at the offending node's location** (so the squiggle lands on the call, not
   the method), with the enclosing subtype name as arg `{1}`.

   Skip descent into nested lambdas / local functions / anonymous methods when they are passed
   out of the method (same scope-boundary guard `IsInsideValueTaskMethod` uses) — a delegate
   handed to another component is not part of the synchronous replay path we can reason about.

`Initialize`: `EnableConcurrentExecution()` +
`ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`, exactly as the ValueTask
analyzer does. Use `SemanticModel`s obtained per syntax tree and cache them in a small
`Dictionary<SyntaxTree, SemanticModel>` for the duration of one type analysis to avoid repeated
`Compilation.GetSemanticModel` calls across partial parts.

### What triggers QRK0040
- A banned API used directly in the body of a `TransitionState` override of a
  `JournaledGrain<,>` subclass.
- The same banned API used in a `private`/`internal` helper **declared on the same grain type**
  that `TransitionState` calls (directly or transitively within the type).

### What does NOT trigger (honest limits)

State these plainly in the wiki and in the spec — the analyzer is deliberately shallow:

- **Cross-type calls are invisible.** If `TransitionState` calls
  `MyDomainRules.Apply(state, e)` on another type, or a static utility, or an injected service,
  nondeterminism inside that callee is **not** detected. Reachability stops at the type
  boundary. This is the primary, documented limitation.
- **Virtual/interface dispatch** on a member is not followed to its implementations.
- **Delegates/lambdas** stored in fields or passed elsewhere are not analyzed.
- **Base-class methods** (other than the `TransitionState` override itself) inherited from a
  further base are not walked.
- **Constructors / field initializers** are not part of the `TransitionState` reachable set.
- Nondeterminism laundered through a captured local (`var now = DateTime.UtcNow;` outside
  `TransitionState`, then read inside) is not tracked — no dataflow analysis.

Consequence: QRK0040 catches the *common, direct* footgun (the 80% case the issue describes)
and produces **no false confidence** — the wiki must say "absence of QRK0040 is not a proof of
determinism." This limitation is acceptable because the true fix (capture-at-RaiseEvent) is a
discipline, and the analyzer's job is to nudge, not to verify.

## Fixer decision

**No automated code fixer for QRK0040.** Rejected after evaluating feasibility.

The QRK0030/0031 fixers work because the remediation is a **mechanical token swap** with a 1:1
replacement that preserves semantics (`Task` → `ValueTask`, `Task.CompletedTask` →
`ValueTask.CompletedTask`). QRK0040 has no such local rewrite:

- The correct fix is to **move** value generation out of `TransitionState`, up to each
  `RaiseEvent` call site (potentially several, in different methods), **add a field** to the
  `TEvent` type to carry the captured value, thread it through the event constructor, and read
  that field inside `TransitionState`. That is a multi-node, multi-file, semantics-changing
  refactor — not a fix a `CodeFixProvider` can perform safely or unambiguously.
- There is no defensible "obvious replacement" token. Swapping `DateTime.UtcNow` for some
  injected clock would (a) require a runtime API we are explicitly rejecting, and (b) still be
  wrong — it does not remove the divergence, it hides it.

Instead of a fixer, invest in a rich diagnostic: a message that names the fix ("capture into
the event at RaiseEvent time") and a `HelpLinkUri` to the wiki section with a copy-paste
before/after. This matches QRK0001–0003 (AOT errors ship with no fixer, guidance only).

## Recommended user guidance

**Primary and only recommended fix: capture nondeterminism into the event at `RaiseEvent`
time; never generate it inside `TransitionState`.** This is standard event-sourcing practice
and needs **no new Quark runtime API**.

Before (flagged — divergent on replay):
```csharp
protected override void TransitionState(CartState s, CartEvent e)
{
    if (e is ItemAdded a)
        s.Lines.Add(new Line(Guid.NewGuid(), a.Sku, DateTime.UtcNow)); // QRK0040 x2
}
```

After (deterministic — the value is fixed once, in the event, and replays identically):
```csharp
// Command handler — the ONE place nondeterminism is allowed:
public ValueTask AddItemAsync(string sku)
{
    RaiseEvent(new ItemAdded(sku, LineId: Guid.NewGuid(), At: DateTime.UtcNow));
    return ConfirmEventsAsync();
}

protected override void TransitionState(CartState s, CartEvent e)
{
    if (e is ItemAdded a)
        s.Lines.Add(new Line(a.LineId, a.Sku, a.At)); // pure — reads event fields only
}
```

Why this is the right layer: `RaiseEvent` runs **once**, during live command handling. The
generated id/timestamp is serialized into the persisted event, so replay reads back the exact
same bytes. `TransitionState` becomes a pure `(state, event) → state` fold.

### Why NO injected deterministic helper (rejected surface)

The issue floats "an injected deterministic ID/clock source available only during
`TransitionState`." We reject it:

- It solves nothing the event-capture pattern doesn't already solve, while **adding runtime
  surface**: a scoped clock/id abstraction, an ambient "am I replaying / what seed" the shell
  must thread into every `TransitionState` call, and a seed persisted alongside each event.
- It is itself a footgun — authors would reach for `ctx.NewGuid()` inside `TransitionState`
  and still get subtle bugs if the seed handling is imperfect.
- Durable Functions needs `context.NewGuid()` because its programming model **interleaves**
  nondeterministic value generation with replayed control flow in one method; Quark's model
  cleanly **separates** command handling (`RaiseEvent`) from the fold (`TransitionState`), so
  the separation-of-concerns fix is available for free and is strictly better.

If a first-class deterministic clock is ever wanted, it belongs with the `TimeProvider` timers
work (#118, `2026-07-02-timeprovider-timers-design.md`) at the command layer — not inside the
replay fold. Out of scope here.

## Test plan

New file `tests/Quark.Tests.CodeGenerator/DeterministicReplayAnalyzerTests.cs`, mirroring
`ValueTaskPerformanceAnalyzerTests.cs`: xUnit `[Fact]`s, raw-string `source`, driven through
`AnalyzerTestDriver.Run(source, new DeterministicReplayAnalyzer())`, asserting on
`d.Id == "QRK0040"`. Each source stubs a minimal `JournaledGrain<TState, TEvent>` base (or
relies on the metadata references already including the persistence abstractions — confirm
`GeneratorTestDriver.GetMetadataReferences()` covers `Quark.Persistence.Abstractions`; if not,
inline a matching-namespace stub so the FQN string match still fires).

Positive cases (each asserts QRK0040 fires):
- `Guid.NewGuid()` directly in `TransitionState`.
- `DateTime.UtcNow` / `DateTime.Now` / `DateTime.Today` in `TransitionState`.
- `DateTimeOffset.UtcNow` in `TransitionState`.
- `Environment.TickCount64` in `TransitionState`.
- `new Random()` in `TransitionState`.
- `Random.Shared.Next()` in `TransitionState`.
- `Stopwatch.GetTimestamp()` in `TransitionState`.
- Banned API in a **same-type private helper** called by `TransitionState` (proves
  intra-type reachability).
- Banned API in a **transitive** same-type helper (`TransitionState` → `A()` → `B()`).
- Multiple offenders in one `TransitionState` → multiple diagnostics.

Negative cases (assert QRK0040 does NOT fire):
- Pure `TransitionState` that reads only event fields (`e.At`, `e.LineId`).
- `new Random(seed)` (seeded ctor is deterministic).
- `DateTime.MinValue` / `DateTime.UnixEpoch` (constants, not `Now`/`UtcNow`/`Today`).
- Banned API in a class that does **not** derive from `JournaledGrain<,>`.
- Banned API in a same-type helper that is **not reachable** from `TransitionState`
  (proves reachability pruning, not blanket type scan).
- Banned API in a `RaiseEvent`-adjacent command method (not in the replay path) — must not fire.
- Documented-limit acknowledgement test: banned API in a **separate type** called by
  `TransitionState` does NOT fire (encodes the known limitation so it can't silently regress
  into a false expectation).

Also add `DeterministicReplayAnalyzer` to whatever aggregate "all analyzers report their
supported diagnostics / releases-file is in sync" test exists, if one is present, and update
`AnalyzerReleases.Unshipped.md`.

## Implementation checklist

Ordered, no circular dependencies (analyzer package has no runtime deps):

1. `src/Quark.Analyzers/DeterministicReplayAnalyzer.cs` — new analyzer (descriptor QRK0040,
   `SymbolKind.NamedType` anchor, intra-type reachability BFS, banned-API table, node scan).
2. `src/Quark.Analyzers/AnalyzerReleases.Unshipped.md` — add the QRK0040 row.
3. `wiki/Persistence.md` — add a "Deterministic replay" section: the before/after pattern, the
   QRK0040 description, and the honest-limits paragraph ("absence of QRK0040 is not a proof").
   Ensure the `helpLinkUri` anchor matches.
4. `tests/Quark.Tests.CodeGenerator/DeterministicReplayAnalyzerTests.cs` — the test matrix above.
5. `FEATURES.md` — tick the determinism-guard row (or add one under the analyzers section).
6. (No fixer file. No runtime/persistence change. No new public API. No generator change.)

## Resolved design decisions

- **ID = QRK0040, category `Quark.Determinism`, severity Warning.** Next free decade;
  Warning because the analysis is a heuristic, not sound.
- **QRK0041 reserved**, not shipped, to keep the decade coherent for a future replay-path rule.
- **Anchor on `SymbolKind.NamedType`**, gate on `JournaledGrain<,>` base by FQN string →
  analyzer needs no reference to `Quark.Persistence.Abstractions`.
- **Reachability is intra-type only** and rooted at `TransitionState`. Documented as a limit,
  not a bug. Not a whole-program guarantee.
- **No code fixer** — the fix is a semantic, multi-file refactor with no 1:1 token swap.
- **No injected deterministic helper / runtime API** — the capture-at-`RaiseEvent` pattern is
  strictly better and free; an ambient clock would add surface and remain a footgun.
- **Seeded `new Random(seed)` and `DateTime` constants are allow-listed** (deterministic).
- **Report at the call-site node**, not the method declaration, so the squiggle is precise.

## Dependencies & related work

- **Depends on:** nothing new. `Quark.Analyzers` and its test project
  (`tests/Quark.Tests.CodeGenerator`, via `AnalyzerTestDriver`/`GeneratorTestDriver`) already
  exist and are the direct template.
- **Mirrors:** `ValueTaskPerformanceAnalyzer` (QRK0030/0031) for analyzer structure and
  `ValueTaskPerformanceAnalyzerTests` for test style. Unlike those, ships **without** a fixer.
- **Event Sourcing V2 (planned sibling spec `2026-07-02-event-sourcing-v2-design.md`).** That
  spec was not present in `docs/superpowers/specs/` at authoring time; when it lands, it should
  reference QRK0040 as the static guardrail for whatever `TransitionState`/apply surface V2
  exposes. If V2 renames or restructures `TransitionState`, update this analyzer's method-name
  anchor accordingly (single string constant). Keep the two specs cross-linked.
- **Related — #118 `TimeProvider` timers (`2026-07-02-timeprovider-timers-design.md`).** The
  natural home for any first-class deterministic clock, at the command layer — explicitly NOT
  inside the replay fold. Referenced only to close off the "injected helper" alternative.
- **Prior art referenced by the issue:** azure-functions-durable-extension#377 (replay-safe
  `context.NewGuid()`), Azure/durabletask#443 (nondeterminism checking). Quark's model lets us
  prefer the separation-of-concerns fix over a replay-aware helper.
