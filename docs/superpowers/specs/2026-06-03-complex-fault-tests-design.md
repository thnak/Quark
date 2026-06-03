# Complex Fault-Tolerance Tests Design

**Date:** 2026-06-03  
**Status:** Approved  
**Scope:** `Quark.Tests.Fault` (in-process) + `Quark.Tests.Fault.Integration` (Testcontainers)

---

## Goal

Add production-realistic tests that verify Quark's fault tolerance and recovery behavior under cascading failures across transport, persistence, and grain activation simultaneously.

---

## Test Grain Domain

A job-processing pipeline modelling a realistic orchestrator/worker pattern:

```
IOrderOrchestratorGrain  (key = orderId : string)
  └─ fans out to N × IWorkerGrain  (key = workerId : string)
       └─ each has IPersistentGrain<WorkerState>
```

**WorkerState**
```csharp
[GenerateSerializer]
record WorkerState
{
    [Id(0)] public string JobId { get; init; } = "";
    [Id(1)] public WorkerStatus Status { get; init; } = WorkerStatus.Idle;
    [Id(2)] public int RetryCount { get; init; }
    [Id(3)] public DateTimeOffset? ProcessedAt { get; init; }
}

enum WorkerStatus { Idle, Processing, Completed, Failed }
```

**OrchestratorState**
```csharp
[GenerateSerializer]
record OrchestratorState
{
    [Id(0)] public string[] WorkerIds { get; init; } = [];
    [Id(1)] public int CompletionCount { get; init; }
    [Id(2)] public OrchestratorStatus Status { get; init; } = OrchestratorStatus.Pending;
}

enum OrchestratorStatus { Pending, Processing, Completed, Failed }
```

**Behavior:**
- `OrderOrchestratorGrain.ProcessAsync(workerIds[])` fans out to all workers in parallel, persists its own state, collects results.
- Failed workers are retried up to 3 times. After 3 failures the orchestrator marks the order `Failed`.
- `WorkerGrain.DoWorkAsync()` increments its counter, persists state, and resolves a `IWorkerBehavior` from DI to simulate injected failures.

---

## Fault Injection Infrastructure

### Decorator types

Each wraps an existing Quark abstraction and holds a `FaultPlan`:

| Decorator | Wraps |
|---|---|
| `FaultInjectingGrainStorage` | `IGrainStorage` |
| `FaultInjectingTransport` | `ITransport` / `ITransportConnection` |
| `FaultInjectingGrainActivator` | `IGrainActivator` |

### FaultRule model

```csharp
record FaultRule(
    FaultTrigger Trigger,   // OnNthCall(n), AfterDelay(ts), Always, Never
    FaultKind Kind,         // Throw, Timeout, PartialWrite, ReturnStale
    string? Filter = null   // optional grain type or method name pattern
);

// Trigger variants
abstract record FaultTrigger;
record OnNthCall(int N) : FaultTrigger;
record AfterDelay(TimeSpan Delay) : FaultTrigger;
record Always : FaultTrigger;
record Never : FaultTrigger;

// Kind variants
abstract record FaultKind;
record Throw<TException>() : FaultKind where TException : Exception, new();
record Timeout(TimeSpan Duration) : FaultKind;
record PartialWrite : FaultKind;
record ReturnStale : FaultKind;
```

### Fluent builder

Registered in test DI only — production code never references this:

```csharp
services.AddFaultScenario(scenario => scenario
    .OnStorage(s => s
        .OnNthWrite(3).Throw<StorageException>())
    .OnTransport(t => t
        .After(TimeSpan.FromMilliseconds(100)).Drop())
    .OnActivation(a => a
        .ForGrainType<WorkerGrain>()
        .OnNthActivation(2).Throw<InvalidOperationException>())
);
```

Each decorator counts calls internally and fires the first matching rule. Rules are consumed in order (queue semantics) unless marked `Always`.

---

## Test Scenarios

### In-process suite — `Quark.Tests.Fault`

Trait: `[Trait("category", "fault")]`  
No external dependencies. All faults injected via decorator fakes.

| Test | Faults Injected | Expected Outcome |
|---|---|---|
| `Storage_FailOnWrite_OrchestratorRetries` | Storage throws on 1st orchestrator write | Orchestrator retries, state eventually consistent |
| `Storage_FailOnRead_WorkerReactivatesClean` | Storage read returns stale data on reactivation | Worker detects stale (`Status == Processing` on load = incomplete prior run), resets to `Idle` state |
| `Transport_DropMidFanout_OrchestratorHandlesPartialResults` | Transport drops 1 of 3 worker calls | Orchestrator marks that worker failed, continues others |
| `Activation_WorkerCrashMidCall_OrchestratorReceivesException` | Worker `OnActivateAsync` throws on 2nd activation | Orchestrator catches, increments retry count |
| `Cascading_StorageFail_Then_ActivationCrash` | Storage fails on write, then worker crashes on retry | Orchestrator marks order `Failed` after 3 retries |
| `Cascading_TransportDrop_Then_StorageFail_Then_Reactivation` | Transport drops → storage fails → grain reactivates | Full recovery: state survives, order eventually completes |

### Testcontainers suite — `Quark.Tests.Fault.Integration`

Trait: `[Trait("category", "fault-integration")]`  
Requires Docker. Skipped when unavailable via `DockerNotAvailableException` guard.  
Uses real Redis via `Testcontainers.Redis`.

| Test | What real infrastructure validates |
|---|---|
| `Redis_ConnectionLostMidWrite_GrainReactivatesConsistently` | StackExchange.Redis reconnect + Quark retry semantics agree |
| `Redis_SlowWrite_TimeoutPropagatesCorrectly` | Real network timeout confirms timeout values are production-realistic |
| `FullPipeline_CascadingFaults_OrderEventuallyCompletes` | Orchestrator + 3 workers + real Redis + transport fault injection end-to-end |

---

## Project Structure

```
tests/
  Quark.Tests.Fault/
    FaultScenario/
      FaultRule.cs              ← FaultTrigger, FaultKind, FaultRule records
      FaultScenarioBuilder.cs   ← fluent builder + DI extension
    Fakes/
      FaultInjectingGrainStorage.cs
      FaultInjectingTransport.cs
      FaultInjectingGrainActivator.cs
    Grains/
      IOrderOrchestratorGrain.cs
      OrderOrchestratorGrain.cs
      IWorkerGrain.cs
      WorkerGrain.cs
      WorkerState.cs
      OrchestratorState.cs
      IWorkerBehavior.cs        ← seam for injecting per-worker behavior
    Tests/
      StorageFaultTests.cs
      TransportFaultTests.cs
      ActivationFaultTests.cs
      CascadingFaultTests.cs
  Quark.Tests.Fault.Integration/
    FaultIntegrationTests.cs
```

---

## Key Constraints

- Fault decorators are registered only in test DI — zero production footprint.
- All in-process tests must complete in under 5 seconds each (no real sleeps; use `FakeClock` for timeouts).
- Testcontainers tests are gated by `[Trait("category", "fault-integration")]` so CI can opt in/out.
- `[GenerateSerializer]` + `[Id]` required on all grain state types (AOT constraint).
- No reflection, no assembly scanning — follow existing Quark AOT rules.
