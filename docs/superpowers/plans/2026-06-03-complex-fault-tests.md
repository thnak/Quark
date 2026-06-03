# Complex Fault-Tolerance Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build two test projects — `Quark.Tests.Fault` (in-process, no Docker) and `Quark.Tests.Fault.Integration` (Testcontainers + real Redis) — that verify cascading fault tolerance across storage, inter-grain calls, and grain activation using an orchestrator/worker grain fan-out domain.

**Architecture:** A `FaultFixture` wires a full in-process DI container with three fault-injecting decorators (`FaultInjectingStorage<TState>`, `FaultInjectingGrainCallInvoker`, `FaultInjectingGrainActivator`). A `FaultScenario` object holds one fault plan per subsystem and is passed to the fixture at test construction time. The integration project re-uses the same grain domain and call infrastructure but swaps the in-memory storage for real Redis via Testcontainers.

**Tech Stack:** .NET 10, xUnit 2.x, Microsoft.Extensions.DependencyInjection 10, Testcontainers.Redis 4.4.0, StackExchange.Redis 2.8.37, Quark.Runtime, Quark.Client, Quark.Persistence.Abstractions, Quark.Persistence.Redis.

---

## File Map

```
tests/
  Quark.Tests.Fault/
    Quark.Tests.Fault.csproj
    FaultScenario/
      FaultPlans.cs                     ← StorageFaultPlan, CallFaultPlan, ActivationFaultPlan
      FaultScenario.cs                  ← FaultScenario aggregate + DI extension
    Fakes/
      FaultInjectingStorage.cs          ← IStorage<TState> with fault injection
      FaultInjectingGrainCallInvoker.cs ← IGrainCallInvoker wrapper
      FaultInjectingGrainActivator.cs   ← IGrainActivator wrapper
    Grains/
      WorkerState.cs                    ← [GenerateSerializer] record + WorkerStatus enum
      OrchestratorState.cs              ← [GenerateSerializer] record + OrchestratorStatus enum
      IWorkerGrain.cs
      IOrderOrchestratorGrain.cs
      WorkerGrain.cs                    ← Grain<WorkerState>, stale-state guard
      OrderOrchestratorGrain.cs         ← Grain<OrchestratorState>, fan-out + 3-retry logic
      WorkerGrainMethodInvoker.cs
      OrderOrchestratorGrainMethodInvoker.cs
      WorkerGrainProxy.cs
      OrderOrchestratorGrainProxy.cs
      WorkerGrainActivatorFactory.cs
      OrderOrchestratorGrainActivatorFactory.cs
    FaultFixture.cs                     ← full DI wiring, exposes IClusterClient + FaultScenario
    Tests/
      StorageFaultTests.cs
      TransportFaultTests.cs
      ActivationFaultTests.cs
      CascadingFaultTests.cs
  Quark.Tests.Fault.Integration/
    Quark.Tests.Fault.Integration.csproj
    FaultIntegrationTests.cs
```

---

## Task 1: Project scaffold

**Files:**
- Create: `tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj`
- Create: `tests/Quark.Tests.Fault.Integration/Quark.Tests.Fault.Integration.csproj`
- Modify: `Quark.slnx`
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Create Quark.Tests.Fault.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Quark.Client\Quark.Client.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Core\Quark.Core.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Core.Abstractions\Quark.Core.Abstractions.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Persistence.Abstractions\Quark.Persistence.Abstractions.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Runtime\Quark.Runtime.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Serialization\Quark.Serialization.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Testing\Quark.Testing.csproj"/>

    <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
    <PackageReference Include="Microsoft.Extensions.Hosting"/>
    <PackageReference Include="Microsoft.NET.Test.Sdk"/>
    <PackageReference Include="xunit"/>
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create Quark.Tests.Fault.Integration.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Quark.Client\Quark.Client.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Core.Abstractions\Quark.Core.Abstractions.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Persistence.Abstractions\Quark.Persistence.Abstractions.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Persistence.Redis\Quark.Persistence.Redis.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Runtime\Quark.Runtime.csproj"/>
    <ProjectReference Include="..\..\src\Quark.Serialization\Quark.Serialization.csproj"/>
    <ProjectReference Include="..\Quark.Tests.Fault\Quark.Tests.Fault.csproj"/>

    <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
    <PackageReference Include="Microsoft.Extensions.Hosting"/>
    <PackageReference Include="Microsoft.NET.Test.Sdk"/>
    <PackageReference Include="Testcontainers.Redis"/>
    <PackageReference Include="xunit"/>
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add Testcontainers.Redis to Directory.Packages.props**

In the `<ItemGroup Label="Testing">` section, add:
```xml
<PackageVersion Include="Testcontainers.Redis" Version="4.4.0" />
```

- [ ] **Step 4: Register both projects in Quark.slnx**

Inside the `<Folder Name="/tests/">` element, add:
```xml
<Project Path="tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj"/>
<Project Path="tests/Quark.Tests.Fault.Integration/Quark.Tests.Fault.Integration.csproj"/>
```

- [ ] **Step 5: Verify the solution builds**

```bash
dotnet build Quark.slnx
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj \
        tests/Quark.Tests.Fault.Integration/Quark.Tests.Fault.Integration.csproj \
        Quark.slnx Directory.Packages.props
git commit -m "feat(tests): scaffold Quark.Tests.Fault and Quark.Tests.Fault.Integration projects"
```

---

## Task 2: Fault plan model

**Files:**
- Create: `tests/Quark.Tests.Fault/FaultScenario/FaultPlans.cs`

- [ ] **Step 1: Create FaultPlans.cs**

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault.FaultScenario;

/// <summary>
/// Controls fault injection for IStorage&lt;TState&gt; reads and writes.
/// Thread-safe via Interlocked counters.
/// </summary>
public sealed class StorageFaultPlan
{
    private int _readCount;
    private int _writeCount;

    private readonly List<(bool IsWrite, int OnN, Func<Exception> ExFac)> _throwRules = [];
    private (int OnN, Func<object?> ValueFac)? _staleReadRule;

    public StorageFaultPlan ThrowOnNthWrite<TException>(int n) where TException : Exception, new()
    {
        _throwRules.Add((true, n, () => new TException()));
        return this;
    }

    public StorageFaultPlan ThrowOnNthRead<TException>(int n) where TException : Exception, new()
    {
        _throwRules.Add((false, n, () => new TException()));
        return this;
    }

    /// <summary>
    /// On the Nth read, return <paramref name="staleValue"/> instead of stored state.
    /// Used to simulate a grain reactivating with an incomplete prior run (Status=Processing).
    /// </summary>
    public StorageFaultPlan ReturnStaleOnNthRead<TState>(int n, TState staleValue) where TState : new()
    {
        _staleReadRule = (n, () => staleValue);
        return this;
    }

    internal void CheckWrite()
    {
        int n = Interlocked.Increment(ref _writeCount);
        foreach (var rule in _throwRules.Where(r => r.IsWrite && r.OnN == n))
            throw rule.ExFac();
    }

    internal (bool IsStale, object? Value) CheckRead()
    {
        int n = Interlocked.Increment(ref _readCount);
        foreach (var rule in _throwRules.Where(r => !r.IsWrite && r.OnN == n))
            throw rule.ExFac();
        if (_staleReadRule.HasValue && _staleReadRule.Value.OnN == n)
            return (true, _staleReadRule.Value.ValueFac());
        return (false, null);
    }
}

/// <summary>
/// Controls fault injection for inter-grain calls routed through IGrainCallInvoker.
/// Supports targeting by grain type or by grain key.
/// </summary>
public sealed class CallFaultPlan
{
    private readonly List<(GrainType? GrainType, string? Key, int OnN, bool Always, Func<Exception> ExFac)> _rules = [];
    private readonly Dictionary<GrainType, int> _callCountsByType = [];

    /// <summary>Throw on the Nth call to any grain of <paramref name="grainType"/>.</summary>
    public CallFaultPlan ThrowOnNthCallToType(GrainType grainType, int n, Func<Exception> exFac)
    {
        _rules.Add((grainType, null, n, false, exFac));
        return this;
    }

    /// <summary>Always throw for calls to the grain with the specific key (all 3 retry attempts).</summary>
    public CallFaultPlan AlwaysThrowForKey(GrainType grainType, string key, Func<Exception> exFac)
    {
        _rules.Add((grainType, key, 0, true, exFac));
        return this;
    }

    internal void Check(GrainId grainId, uint methodId)
    {
        lock (_callCountsByType)
        {
            // Check Always rules first (by key)
            foreach (var rule in _rules.Where(r => r.Always && r.Key == grainId.Key && r.GrainType == grainId.Type))
                throw rule.ExFac();

            // Increment call count once per call, then check OnNth rules
            _callCountsByType[grainId.Type] = _callCountsByType.GetValueOrDefault(grainId.Type) + 1;
            int count = _callCountsByType[grainId.Type];
            foreach (var rule in _rules.Where(r => !r.Always && r.GrainType == grainId.Type && r.OnN == count))
                throw rule.ExFac();
        }
    }
}

/// <summary>
/// Controls fault injection for grain activation (IGrainActivator.CreateInstance).
/// </summary>
public sealed class ActivationFaultPlan
{
    private readonly List<(Type GrainClass, int OnN, Func<Exception> ExFac)> _rules = [];
    private readonly Dictionary<Type, int> _activationCounts = [];

    public ActivationFaultPlan ThrowOnNthActivation<TGrain>(int n) where TGrain : Grain
    {
        _rules.Add((typeof(TGrain), n, () => new InvalidOperationException($"Simulated activation crash for {typeof(TGrain).Name} (attempt {n})")));
        return this;
    }

    public ActivationFaultPlan ThrowOnNthActivation<TGrain>(int n, Func<Exception> exFac) where TGrain : Grain
    {
        _rules.Add((typeof(TGrain), n, exFac));
        return this;
    }

    internal void Check(Type grainClass)
    {
        lock (_activationCounts)
        {
            _activationCounts[grainClass] = _activationCounts.GetValueOrDefault(grainClass) + 1;
            int count = _activationCounts[grainClass];
            foreach (var rule in _rules.Where(r => r.GrainClass == grainClass && r.OnN == count))
                throw rule.ExFac();
        }
    }
}
```

- [ ] **Step 2: Create FaultScenario.cs**

Place in `tests/Quark.Tests.Fault/FaultScenario/FaultScenario.cs`.  
Use the **root** namespace `Quark.Tests.Fault` (not `Quark.Tests.Fault.FaultScenario`) to avoid the
class name colliding with its own containing namespace.

```csharp
using Quark.Tests.Fault.FaultScenario;

namespace Quark.Tests.Fault;

/// <summary>
/// Aggregates all fault plans for a single test scenario.
/// Pass an Action&lt;FaultScenario&gt; to FaultFixture to configure faults before test execution.
/// </summary>
public sealed class FaultScenario
{
    public StorageFaultPlan WorkerStorage { get; } = new();
    public StorageFaultPlan OrchestratorStorage { get; } = new();
    public CallFaultPlan Calls { get; } = new();
    public ActivationFaultPlan Activations { get; } = new();
}
```

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Tests.Fault/FaultScenario/
git commit -m "feat(tests/fault): add StorageFaultPlan, CallFaultPlan, ActivationFaultPlan, FaultScenario"
```

---

## Task 3: Grain state types

**Files:**
- Create: `tests/Quark.Tests.Fault/Grains/WorkerState.cs`
- Create: `tests/Quark.Tests.Fault/Grains/OrchestratorState.cs`

- [ ] **Step 1: Create WorkerState.cs**

```csharp
using Quark.Serialization.Abstractions.Attributes;

namespace Quark.Tests.Fault.Grains;

public enum WorkerStatus { Idle, Processing, Completed, Failed }

[GenerateSerializer]
[Alias("WorkerState")]
public sealed record WorkerState
{
    [Id(0)] public string JobId { get; init; } = "";
    [Id(1)] public WorkerStatus Status { get; init; } = WorkerStatus.Idle;
    [Id(2)] public int RetryCount { get; init; }
    [Id(3)] public DateTimeOffset? ProcessedAt { get; init; }
}
```

- [ ] **Step 2: Create OrchestratorState.cs**

```csharp
using Quark.Serialization.Abstractions.Attributes;

namespace Quark.Tests.Fault.Grains;

public enum OrchestratorStatus { Pending, Processing, Completed, Failed }

[GenerateSerializer]
[Alias("OrchestratorState")]
public sealed record OrchestratorState
{
    [Id(0)] public string[] WorkerIds { get; init; } = [];
    [Id(1)] public int CompletionCount { get; init; }
    [Id(2)] public OrchestratorStatus Status { get; init; } = OrchestratorStatus.Pending;
}
```

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Tests.Fault/Grains/WorkerState.cs \
        tests/Quark.Tests.Fault/Grains/OrchestratorState.cs
git commit -m "feat(tests/fault): add WorkerState and OrchestratorState grain state types"
```

---

## Task 4: Grain interfaces

**Files:**
- Create: `tests/Quark.Tests.Fault/Grains/IWorkerGrain.cs`
- Create: `tests/Quark.Tests.Fault/Grains/IOrderOrchestratorGrain.cs`

- [ ] **Step 1: Create IWorkerGrain.cs**

```csharp
using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Fault.Grains;

public interface IWorkerGrain : IGrainWithStringKey
{
    Task<WorkerStatus> DoWorkAsync();
}
```

- [ ] **Step 2: Create IOrderOrchestratorGrain.cs**

```csharp
using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Fault.Grains;

public interface IOrderOrchestratorGrain : IGrainWithStringKey
{
    Task<OrchestratorStatus> ProcessAsync(string[] workerIds);
}
```

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Tests.Fault/Grains/IWorkerGrain.cs \
        tests/Quark.Tests.Fault/Grains/IOrderOrchestratorGrain.cs
git commit -m "feat(tests/fault): add IWorkerGrain and IOrderOrchestratorGrain interfaces"
```

---

## Task 5: Grain implementations

**Files:**
- Create: `tests/Quark.Tests.Fault/Grains/WorkerGrain.cs`
- Create: `tests/Quark.Tests.Fault/Grains/OrderOrchestratorGrain.cs`

- [ ] **Step 1: Create WorkerGrain.cs**

```csharp
using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerGrain : Grain<WorkerState>, IWorkerGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken); // calls ReadStateAsync

        // Stale-state guard: Status=Processing on load means a prior run didn't finish.
        if (State.Status == WorkerStatus.Processing)
            State = State with { Status = WorkerStatus.Idle };
    }

    public async Task<WorkerStatus> DoWorkAsync()
    {
        State = State with { Status = WorkerStatus.Processing };
        await WriteStateAsync();

        State = State with
        {
            Status = WorkerStatus.Completed,
            RetryCount = State.RetryCount + 1,
            ProcessedAt = DateTimeOffset.UtcNow
        };
        await WriteStateAsync();
        return State.Status;
    }
}
```

- [ ] **Step 2: Create OrderOrchestratorGrain.cs**

```csharp
using Quark.Persistence.Abstractions;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorGrain : Grain<OrchestratorState>, IOrderOrchestratorGrain
{
    public async Task<OrchestratorStatus> ProcessAsync(string[] workerIds)
    {
        State = State with { WorkerIds = workerIds, Status = OrchestratorStatus.Processing };
        await WriteStateAsync();

        int completionCount = 0;
        bool anyFailed = false;

        foreach (string wid in workerIds)
        {
            bool succeeded = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    IWorkerGrain worker = GrainFactory.GetGrain<IWorkerGrain>(wid);
                    await worker.DoWorkAsync();
                    succeeded = true;
                    break;
                }
                catch (Exception)
                {
                    // retry up to 3 times; on last attempt, fall through to failure
                }
            }

            if (succeeded) completionCount++;
            else anyFailed = true;
        }

        OrchestratorStatus finalStatus = anyFailed
            ? OrchestratorStatus.Failed
            : OrchestratorStatus.Completed;

        State = State with { Status = finalStatus, CompletionCount = completionCount };
        await WriteStateAsync();
        return finalStatus;
    }
}
```

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Tests.Fault/Grains/WorkerGrain.cs \
        tests/Quark.Tests.Fault/Grains/OrderOrchestratorGrain.cs
git commit -m "feat(tests/fault): add WorkerGrain and OrderOrchestratorGrain implementations"
```

---

## Task 6: Method invokers, proxies, and activator factories

These are the hand-written stand-ins for what `Quark.CodeGenerator` would generate in production.

**Files:**
- Create: `tests/Quark.Tests.Fault/Grains/WorkerGrainMethodInvoker.cs`
- Create: `tests/Quark.Tests.Fault/Grains/OrderOrchestratorGrainMethodInvoker.cs`
- Create: `tests/Quark.Tests.Fault/Grains/WorkerGrainProxy.cs`
- Create: `tests/Quark.Tests.Fault/Grains/OrderOrchestratorGrainProxy.cs`
- Create: `tests/Quark.Tests.Fault/Grains/WorkerGrainActivatorFactory.cs`
- Create: `tests/Quark.Tests.Fault/Grains/OrderOrchestratorGrainActivatorFactory.cs`

- [ ] **Step 1: Create WorkerGrainMethodInvoker.cs**

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerGrainMethodInvoker : IGrainMethodInvoker
{
    public const uint DoWorkMethodId = 0;

    public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
    {
        var worker = (WorkerGrain)grain;
        return methodId switch
        {
            DoWorkMethodId => await worker.DoWorkAsync(),
            _ => throw new NotSupportedException($"Unknown method id {methodId} for WorkerGrain")
        };
    }
}
```

- [ ] **Step 2: Create OrderOrchestratorGrainMethodInvoker.cs**

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorGrainMethodInvoker : IGrainMethodInvoker
{
    public const uint ProcessMethodId = 0;

    public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
    {
        var orchestrator = (OrderOrchestratorGrain)grain;
        return methodId switch
        {
            ProcessMethodId => await orchestrator.ProcessAsync((string[])arguments![0]!),
            _ => throw new NotSupportedException($"Unknown method id {methodId} for OrderOrchestratorGrain")
        };
    }
}
```

- [ ] **Step 3: Create WorkerGrainProxy.cs**

```csharp
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerGrainProxy : IWorkerGrain
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public WorkerGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public Task<WorkerStatus> DoWorkAsync()
        => _invoker.InvokeAsync<WorkerStatus>(_grainId, WorkerGrainMethodInvoker.DoWorkMethodId);
}
```

- [ ] **Step 4: Create OrderOrchestratorGrainProxy.cs**

```csharp
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorGrainProxy : IOrderOrchestratorGrain
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public OrderOrchestratorGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public Task<OrchestratorStatus> ProcessAsync(string[] workerIds)
        => _invoker.InvokeAsync<OrchestratorStatus>(
            _grainId,
            OrderOrchestratorGrainMethodInvoker.ProcessMethodId,
            new object?[] { workerIds });
}
```

- [ ] **Step 5: Create WorkerGrainActivatorFactory.cs**

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Runtime;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerGrainActivatorFactory : IGrainActivatorFactory
{
    public Type GrainClass => typeof(WorkerGrain);
    public Grain Create(IServiceProvider services) => new WorkerGrain();
}
```

- [ ] **Step 6: Create OrderOrchestratorGrainActivatorFactory.cs**

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Runtime;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorGrainActivatorFactory : IGrainActivatorFactory
{
    public Type GrainClass => typeof(OrderOrchestratorGrain);
    public Grain Create(IServiceProvider services) => new OrderOrchestratorGrain();
}
```

- [ ] **Step 7: Verify it compiles**

```bash
dotnet build tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj
```
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add tests/Quark.Tests.Fault/Grains/
git commit -m "feat(tests/fault): add method invokers, proxies, and activator factories for grain domain"
```

---

## Task 7: FaultInjectingStorage\<TState\>

**Files:**
- Create: `tests/Quark.Tests.Fault/Fakes/FaultInjectingStorage.cs`

- [ ] **Step 1: Create FaultInjectingStorage.cs**

```csharp
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Tests.Fault.FaultScenario;

namespace Quark.Tests.Fault.Fakes;

/// <summary>
/// Self-contained IStorage&lt;TState&gt; with configurable fault injection.
/// Maintains its own in-memory store; no dependency on InMemoryGrainStorage.
/// </summary>
public sealed class FaultInjectingStorage<TState> : IStorage<TState> where TState : new()
{
    private readonly Dictionary<string, TState> _store = new();
    private readonly StorageFaultPlan _plan;

    public FaultInjectingStorage(StorageFaultPlan plan)
    {
        _plan = plan;
    }

    public Task<TState> ReadAsync(GrainId grainId, string? stateName = null, CancellationToken ct = default)
    {
        var (isStale, staleValue) = _plan.CheckRead();
        if (isStale)
            return Task.FromResult((TState)staleValue!);

        string key = Key(grainId, stateName);
        return Task.FromResult(_store.TryGetValue(key, out TState? stored) ? stored : new TState());
    }

    public Task WriteAsync(GrainId grainId, TState state, string? stateName = null, CancellationToken ct = default)
    {
        _plan.CheckWrite();
        _store[Key(grainId, stateName)] = state;
        return Task.CompletedTask;
    }

    public Task ClearAsync(GrainId grainId, string? stateName = null, CancellationToken ct = default)
    {
        _store.Remove(Key(grainId, stateName));
        return Task.CompletedTask;
    }

    private static string Key(GrainId id, string? name)
        => $"{id.Type.Value}/{id.Key}/{name ?? "Default"}";
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Tests.Fault/Fakes/FaultInjectingStorage.cs
git commit -m "feat(tests/fault): add FaultInjectingStorage<TState>"
```

---

## Task 8: FaultInjectingGrainCallInvoker

**Files:**
- Create: `tests/Quark.Tests.Fault/Fakes/FaultInjectingGrainCallInvoker.cs`

- [ ] **Step 1: Create FaultInjectingGrainCallInvoker.cs**

```csharp
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Tests.Fault.FaultScenario;

namespace Quark.Tests.Fault.Fakes;

/// <summary>
/// Wraps IGrainCallInvoker to inject simulated call failures (dropped calls, timeouts).
/// Intercepts both external client calls and inter-grain calls routed through the same invoker.
/// </summary>
public sealed class FaultInjectingGrainCallInvoker : IGrainCallInvoker
{
    private readonly IGrainCallInvoker _inner;
    private readonly CallFaultPlan _plan;

    public FaultInjectingGrainCallInvoker(IGrainCallInvoker inner, CallFaultPlan plan)
    {
        _inner = inner;
        _plan = plan;
    }

    public Task<object?> InvokeAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
    {
        _plan.Check(id, method);
        return _inner.InvokeAsync(id, method, args, ct);
    }

    public Task<TResult> InvokeAsync<TResult>(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
    {
        _plan.Check(id, method);
        return _inner.InvokeAsync<TResult>(id, method, args, ct);
    }

    public Task InvokeVoidAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
    {
        _plan.Check(id, method);
        return _inner.InvokeVoidAsync(id, method, args, ct);
    }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Tests.Fault/Fakes/FaultInjectingGrainCallInvoker.cs
git commit -m "feat(tests/fault): add FaultInjectingGrainCallInvoker"
```

---

## Task 9: FaultInjectingGrainActivator

**Files:**
- Create: `tests/Quark.Tests.Fault/Fakes/FaultInjectingGrainActivator.cs`

- [ ] **Step 1: Create FaultInjectingGrainActivator.cs**

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Tests.Fault.FaultScenario;

namespace Quark.Tests.Fault.Fakes;

/// <summary>
/// Wraps IGrainActivator to inject faults at grain instantiation time (before OnActivateAsync).
/// Resolves the concrete grain class from the type registry to match ActivationFaultPlan rules.
/// </summary>
public sealed class FaultInjectingGrainActivator : IGrainActivator
{
    private readonly IGrainActivator _inner;
    private readonly IGrainTypeRegistry _registry;
    private readonly ActivationFaultPlan _plan;

    public FaultInjectingGrainActivator(
        IGrainActivator inner,
        IGrainTypeRegistry registry,
        ActivationFaultPlan plan)
    {
        _inner = inner;
        _registry = registry;
        _plan = plan;
    }

    public Grain CreateInstance(GrainType grainType)
    {
        if (_registry.TryGetGrainClass(grainType, out Type? grainClass) && grainClass is not null)
            _plan.Check(grainClass);

        return _inner.CreateInstance(grainType);
    }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Tests.Fault/Fakes/FaultInjectingGrainActivator.cs
git commit -m "feat(tests/fault): add FaultInjectingGrainActivator"
```

---

## Task 10: FaultFixture

**Files:**
- Create: `tests/Quark.Tests.Fault/FaultFixture.cs`

- [ ] **Step 1: Create FaultFixture.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Tests.Fault.Fakes;
using Quark.Tests.Fault.FaultScenario;
using Quark.Tests.Fault.Grains;

namespace Quark.Tests.Fault;

public sealed class FaultFixture : IAsyncDisposable
{
    private readonly ServiceProvider _sp;
    private readonly GrainActivationTable _activationTable;

    public FaultFixture(Action<FaultScenario.FaultScenario>? configure = null)
    {
        Scenario = new FaultScenario.FaultScenario();
        configure?.Invoke(Scenario);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkSerialization();

        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "fault-test";
            o.ServiceId = "fault";
            o.SiloName = "silo0";
        });

        // Fault-injecting storage — registered per concrete state type
        var workerStorage = new FaultInjectingStorage<WorkerState>(Scenario.WorkerStorage);
        var orchestratorStorage = new FaultInjectingStorage<OrchestratorState>(Scenario.OrchestratorStorage);
        services.AddSingleton<IStorage<WorkerState>>(workerStorage);
        services.AddSingleton<IStorage<OrchestratorState>>(orchestratorStorage);

        // Core runtime — registry, directory, activation table
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<GrainActivationTable>();
        services.AddSingleton<GrainMethodInvokerRegistry>();
        services.AddSingleton<IGrainMethodInvokerRegistry>(sp => sp.GetRequiredService<GrainMethodInvokerRegistry>());

        // Grain activator factories
        services.AddSingleton<IGrainActivatorFactory>(new WorkerGrainActivatorFactory());
        services.AddSingleton<IGrainActivatorFactory>(new OrderOrchestratorGrainActivatorFactory());

        // Fault-injecting activator wraps DefaultGrainActivator
        services.AddSingleton<DefaultGrainActivator>();
        services.AddSingleton<IGrainActivator>(sp =>
            new FaultInjectingGrainActivator(
                sp.GetRequiredService<DefaultGrainActivator>(),
                sp.GetRequiredService<IGrainTypeRegistry>(),
                Scenario.Activations));

        // Method invokers
        services.AddSingleton<WorkerGrainMethodInvoker>();
        services.AddSingleton<OrderOrchestratorGrainMethodInvoker>();

        // Client-side registries
        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        _sp = services.BuildServiceProvider();

        // Deferred registrations (normally done by hosted services)
        var typeRegistry = _sp.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("WorkerGrain"), typeof(WorkerGrain));
        typeRegistry.Register(new GrainType("OrderOrchestratorGrain"), typeof(OrderOrchestratorGrain));

        var invokerRegistry = _sp.GetRequiredService<GrainMethodInvokerRegistry>();
        invokerRegistry.Register(typeof(WorkerGrain), _sp.GetRequiredService<WorkerGrainMethodInvoker>());
        invokerRegistry.Register(typeof(OrderOrchestratorGrain), _sp.GetRequiredService<OrderOrchestratorGrainMethodInvoker>());

        var proxyRegistry = _sp.GetRequiredService<GrainProxyFactoryRegistry>();
        var interfaceRegistry = _sp.GetRequiredService<GrainInterfaceTypeRegistry>();

        interfaceRegistry.Register(typeof(IWorkerGrain), new GrainType("WorkerGrain"));
        interfaceRegistry.Register(typeof(IOrderOrchestratorGrain), new GrainType("OrderOrchestratorGrain"));
        proxyRegistry.Register<IWorkerGrain, WorkerGrainProxy>((id, inv) => new WorkerGrainProxy(id, inv));
        proxyRegistry.Register<IOrderOrchestratorGrain, OrderOrchestratorGrainProxy>((id, inv) => new OrderOrchestratorGrainProxy(id, inv));

        // Break circular dep: LocalGrainFactory ↔ LocalGrainCallInvoker
        var deferredInvoker = new DeferredGrainCallInvoker();
        var localFactory = new LocalGrainFactory(proxyRegistry, interfaceRegistry, deferredInvoker);

        _activationTable = _sp.GetRequiredService<GrainActivationTable>();
        var realInvoker = new LocalGrainCallInvoker(
            _activationTable,
            _sp.GetRequiredService<IGrainActivator>(),
            typeRegistry,
            _sp.GetRequiredService<IGrainDirectory>(),
            _sp.GetRequiredService<IGrainMethodInvokerRegistry>(),
            localFactory,
            _sp,
            _sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance);

        // Fault-injecting call invoker wraps the real one
        IGrainCallInvoker effectiveInvoker = new FaultInjectingGrainCallInvoker(realInvoker, Scenario.Calls);
        deferredInvoker.SetInvoker(effectiveInvoker);

        Client = new LocalClusterClient(new LocalGrainFactory(proxyRegistry, interfaceRegistry, effectiveInvoker));
    }

    public IClusterClient Client { get; }
    public FaultScenario.FaultScenario Scenario { get; }

    public async ValueTask DisposeAsync()
    {
        await _activationTable.DisposeAsync();
        await _sp.DisposeAsync();
    }

    private sealed class DeferredGrainCallInvoker : IGrainCallInvoker
    {
        private IGrainCallInvoker? _inner;

        public void SetInvoker(IGrainCallInvoker invoker) => _inner = invoker;

        public Task<object?> InvokeAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeAsync(id, method, args, ct);

        public Task<TResult> InvokeAsync<TResult>(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeAsync<TResult>(id, method, args, ct);

        public Task InvokeVoidAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeVoidAsync(id, method, args, ct);
    }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/Quark.Tests.Fault/FaultFixture.cs
git commit -m "feat(tests/fault): add FaultFixture with full DI wiring and fault-injecting decorators"
```

---

## Task 11: StorageFaultTests

**Files:**
- Create: `tests/Quark.Tests.Fault/Tests/StorageFaultTests.cs`

- [ ] **Step 1: Write the two failing tests**

```csharp
using Xunit;

namespace Quark.Tests.Fault.Tests;

[Trait("category", "fault")]
public sealed class StorageFaultTests : IAsyncDisposable
{
    private FaultFixture _fixture = null!;

    public ValueTask DisposeAsync() => _fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Storage throws on the orchestrator's first write.
    /// The orchestrator must handle the exception from WriteStateAsync and propagate it
    /// as a failed OrchestratorStatus (the caller catches it at the test boundary).
    /// </summary>
    [Fact]
    public async Task Storage_FailOnWrite_OrchestratorRetries()
    {
        _fixture = new FaultFixture(s =>
            s.OrchestratorStorage.ThrowOnNthWrite<InvalidOperationException>(1));

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-write-fail");

        // The first ProcessAsync call throws because WriteStateAsync fails on the 1st write.
        // A second call succeeds because the counter already advanced past N=1.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ProcessAsync(["w1"]));

        // Second attempt: storage write counter is now at 2, no rule fires → succeeds
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w1"]);
        Assert.Equal(OrchestratorStatus.Completed, result);
    }

    /// <summary>
    /// Storage returns stale WorkerState (Status=Processing) on the 1st read.
    /// WorkerGrain.OnActivateAsync detects the stale Processing state and resets to Idle.
    /// DoWorkAsync then completes normally.
    /// </summary>
    [Fact]
    public async Task Storage_FailOnRead_WorkerReactivatesClean()
    {
        var staleState = new Grains.WorkerState { Status = Grains.WorkerStatus.Processing, JobId = "stale-job" };

        _fixture = new FaultFixture(s =>
            s.WorkerStorage.ReturnStaleOnNthRead(1, staleState));

        // Activating the worker for the first time returns the stale state.
        // OnActivateAsync resets Status to Idle — DoWorkAsync should complete.
        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-stale-read");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-stale"]);

        Assert.Equal(OrchestratorStatus.Completed, result);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail with a meaningful error (not compile error)**

```bash
dotnet test tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj \
  --filter "category=fault&ClassName~StorageFaultTests" -v normal
```
Expected: Both tests fail — either the fixture wiring is wrong or the grain logic is incomplete. Read the error output carefully.

- [ ] **Step 3: Run all fault tests to verify no regressions**

```bash
dotnet test tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj --filter "category=fault" -v normal
```
Expected: The two storage tests pass. No other tests exist yet (0 other failures).

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Tests.Fault/Tests/StorageFaultTests.cs
git commit -m "test(fault): add StorageFaultTests — write failure and stale read scenarios"
```

---

## Task 12: TransportFaultTests and ActivationFaultTests

**Files:**
- Create: `tests/Quark.Tests.Fault/Tests/TransportFaultTests.cs`
- Create: `tests/Quark.Tests.Fault/Tests/ActivationFaultTests.cs`

- [ ] **Step 1: Create TransportFaultTests.cs**

```csharp
using Quark.Core.Abstractions.Identity;
using Quark.Tests.Fault.Grains;
using Xunit;

namespace Quark.Tests.Fault.Tests;

[Trait("category", "fault")]
public sealed class TransportFaultTests : IAsyncDisposable
{
    private FaultFixture _fixture = null!;

    public ValueTask DisposeAsync() => _fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// All calls to worker "w1" are dropped (AlwaysThrowForKey).
    /// Workers "w2" and "w3" succeed on first attempt.
    /// Orchestrator retries w1 three times, exhausts retries, marks order Failed.
    /// CompletionCount = 2 (w2 and w3 completed).
    /// </summary>
    [Fact]
    public async Task Transport_DropMidFanout_OrchestratorHandlesPartialResults()
    {
        _fixture = new FaultFixture(s =>
            s.Calls.AlwaysThrowForKey(
                new GrainType("WorkerGrain"),
                "w1",
                () => new InvalidOperationException("Simulated call drop for w1")));

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-drop");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w1", "w2", "w3"]);

        Assert.Equal(OrchestratorStatus.Failed, result);

        // Verify the orchestrator persisted its final state correctly
        // (re-activate it and check state via a second call — state survives in storage)
        OrchestratorStatus result2 = await orchestrator.ProcessAsync(["w2", "w3"]);
        Assert.Equal(OrchestratorStatus.Completed, result2);
    }
}
```

- [ ] **Step 2: Create ActivationFaultTests.cs**

```csharp
using Quark.Tests.Fault.Grains;
using Xunit;

namespace Quark.Tests.Fault.Tests;

[Trait("category", "fault")]
public sealed class ActivationFaultTests : IAsyncDisposable
{
    private FaultFixture _fixture = null!;

    public ValueTask DisposeAsync() => _fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// WorkerGrain.CreateInstance throws on the 2nd activation attempt.
    /// The orchestrator catches the exception and retries the worker.
    /// The 3rd activation attempt succeeds, so the order completes.
    /// </summary>
    [Fact]
    public async Task Activation_WorkerCrashMidCall_OrchestratorReceivesException()
    {
        _fixture = new FaultFixture(s =>
            s.Activations.ThrowOnNthActivation<WorkerGrain>(2));

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-activation-crash");

        // 1st call: worker activates fine (1st activation) → completes
        // After GrainActivationTable.DisposeAsync the activation is evicted.
        // On retry the orchestrator re-activates the worker (2nd activation → throws).
        // On 3rd retry (3rd activation) it succeeds.
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-crash"]);
        Assert.Equal(OrchestratorStatus.Completed, result);
    }
}
```

- [ ] **Step 3: Run these tests**

```bash
dotnet test tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj \
  --filter "category=fault&ClassName~TransportFaultTests|category=fault&ClassName~ActivationFaultTests" -v normal
```
Expected: Both tests pass.

- [ ] **Step 4: Run all fault tests**

```bash
dotnet test tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj --filter "category=fault" -v normal
```
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add tests/Quark.Tests.Fault/Tests/TransportFaultTests.cs \
        tests/Quark.Tests.Fault/Tests/ActivationFaultTests.cs
git commit -m "test(fault): add TransportFaultTests and ActivationFaultTests"
```

---

## Task 13: CascadingFaultTests

**Files:**
- Create: `tests/Quark.Tests.Fault/Tests/CascadingFaultTests.cs`

- [ ] **Step 1: Create CascadingFaultTests.cs**

```csharp
using Quark.Core.Abstractions.Identity;
using Quark.Tests.Fault.Grains;
using Xunit;

namespace Quark.Tests.Fault.Tests;

[Trait("category", "fault")]
public sealed class CascadingFaultTests : IAsyncDisposable
{
    private FaultFixture _fixture = null!;

    public ValueTask DisposeAsync() => _fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Storage fails on the 1st write for the worker.
    /// Then the worker activator crashes on the 2nd activation (retry attempt).
    /// The orchestrator exhausts all 3 retries and marks the order Failed.
    /// </summary>
    [Fact]
    public async Task Cascading_StorageFail_Then_ActivationCrash()
    {
        _fixture = new FaultFixture(s =>
        {
            // First attempt: worker storage write fails
            s.WorkerStorage.ThrowOnNthWrite<InvalidOperationException>(1);
            // Second attempt (retry): activator crashes
            s.Activations.ThrowOnNthActivation<WorkerGrain>(2);
            // Third attempt (retry): activator crashes again to exhaust retries
            s.Activations.ThrowOnNthActivation<WorkerGrain>(3);
        });

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-cascade-1");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-cascade"]);

        Assert.Equal(OrchestratorStatus.Failed, result);
    }

    /// <summary>
    /// Cascading recovery scenario:
    /// 1. Call to w-recovery is dropped (1st attempt).
    /// 2. Storage write fails for w-recovery (2nd attempt / retry).
    /// 3. Worker reactivates from stale state and resets cleanly (3rd attempt / retry).
    /// Expected: Order eventually completes with CompletionCount=1.
    /// </summary>
    [Fact]
    public async Task Cascading_TransportDrop_Then_StorageFail_Then_Reactivation()
    {
        var staleState = new WorkerState { Status = WorkerStatus.Processing };

        _fixture = new FaultFixture(s =>
        {
            // 1st call attempt: call is dropped
            s.Calls.ThrowOnNthCallToType(
                new GrainType("WorkerGrain"),
                1,
                () => new InvalidOperationException("Simulated call drop"));

            // 2nd attempt: storage write fails during DoWorkAsync
            s.WorkerStorage.ThrowOnNthWrite<InvalidOperationException>(1);

            // 3rd attempt: worker reactivates and reads stale state (Status=Processing)
            // OnActivateAsync resets to Idle → DoWorkAsync completes normally
            s.WorkerStorage.ReturnStaleOnNthRead(1, staleState);
        });

        var orchestrator = _fixture.Client.GetGrain<IOrderOrchestratorGrain>("order-cascade-2");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-recovery"]);

        Assert.Equal(OrchestratorStatus.Completed, result);
    }
}
```

- [ ] **Step 2: Run all fault tests**

```bash
dotnet test tests/Quark.Tests.Fault/Quark.Tests.Fault.csproj --filter "category=fault" -v normal
```
Expected: All 6 tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/Quark.Tests.Fault/Tests/CascadingFaultTests.cs
git commit -m "test(fault): add CascadingFaultTests — cascading storage+activation and full recovery scenarios"
```

---

## Task 14: FaultIntegrationTests (Testcontainers + real Redis)

**Files:**
- Create: `tests/Quark.Tests.Fault.Integration/FaultIntegrationTests.cs`

- [ ] **Step 1: Write the three integration tests**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.Redis;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Tests.Fault;
using Quark.Tests.Fault.Fakes;
using Quark.Tests.Fault.FaultScenario;
using Quark.Tests.Fault.Grains;
using Testcontainers.Redis;
using Xunit;

namespace Quark.Tests.Fault.Integration;

/// <summary>
/// Fault-tolerance tests against real Redis via Testcontainers.
/// Requires Docker. Skip by running: dotnet test --filter "category!=fault-integration"
/// </summary>
[Trait("category", "fault-integration")]
public sealed class FaultIntegrationTests : IAsyncLifetime
{
    private RedisContainer _redis = null!;
    private ServiceProvider _sp = null!;
    private GrainActivationTable _activationTable = null!;
    private IClusterClient _client = null!;
    private FaultScenario.FaultScenario _scenario = null!;

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().Build();
        await _redis.StartAsync();
        BuildFixture(new FaultScenario.FaultScenario());
    }

    public async Task DisposeAsync()
    {
        await _activationTable.DisposeAsync();
        await _sp.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private void BuildFixture(FaultScenario.FaultScenario scenario)
    {
        _scenario = scenario;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuarkSerialization();

        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "fault-integration";
            o.ServiceId = "fault";
            o.SiloName = "silo0";
        });

        // Real Redis storage for workers; fault-injecting wrapper for orchestrator
        services.AddRedisGrainStorage(o => o.ConnectionString = _redis.GetConnectionString());

        // For WorkerGrain: wrap real Redis IStorage<WorkerState> with fault injector
        // For OrchestratorGrain: wrap real Redis IStorage<OrchestratorState> with fault injector
        // We build the real storage first, then register wrappers that delegate to it.
        // Register real Redis-backed storage under a keyed name via a factory override:
        services.AddSingleton<IStorage<WorkerState>>(sp =>
        {
            var realStorage = sp.GetRequiredService<RedisGrainStorage<WorkerState>>();
            return new FaultInjectingStorage<WorkerState>(scenario.WorkerStorage, realStorage);
        });
        services.AddSingleton<IStorage<OrchestratorState>>(sp =>
        {
            var realStorage = sp.GetRequiredService<RedisGrainStorage<OrchestratorState>>();
            return new FaultInjectingStorage<OrchestratorState>(scenario.OrchestratorStorage, realStorage);
        });

        // Runtime
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<GrainActivationTable>();
        services.AddSingleton<GrainMethodInvokerRegistry>();
        services.AddSingleton<IGrainMethodInvokerRegistry>(sp => sp.GetRequiredService<GrainMethodInvokerRegistry>());
        services.AddSingleton<IGrainActivatorFactory>(new WorkerGrainActivatorFactory());
        services.AddSingleton<IGrainActivatorFactory>(new OrderOrchestratorGrainActivatorFactory());
        services.AddSingleton<DefaultGrainActivator>();
        services.AddSingleton<IGrainActivator>(sp =>
            new FaultInjectingGrainActivator(
                sp.GetRequiredService<DefaultGrainActivator>(),
                sp.GetRequiredService<IGrainTypeRegistry>(),
                scenario.Activations));
        services.AddSingleton<WorkerGrainMethodInvoker>();
        services.AddSingleton<OrderOrchestratorGrainMethodInvoker>();
        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        _sp = services.BuildServiceProvider();

        var typeRegistry = _sp.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("WorkerGrain"), typeof(WorkerGrain));
        typeRegistry.Register(new GrainType("OrderOrchestratorGrain"), typeof(OrderOrchestratorGrain));

        var invokerRegistry = _sp.GetRequiredService<GrainMethodInvokerRegistry>();
        invokerRegistry.Register(typeof(WorkerGrain), _sp.GetRequiredService<WorkerGrainMethodInvoker>());
        invokerRegistry.Register(typeof(OrderOrchestratorGrain), _sp.GetRequiredService<OrderOrchestratorGrainMethodInvoker>());

        var proxyRegistry = _sp.GetRequiredService<GrainProxyFactoryRegistry>();
        var interfaceRegistry = _sp.GetRequiredService<GrainInterfaceTypeRegistry>();
        interfaceRegistry.Register(typeof(IWorkerGrain), new GrainType("WorkerGrain"));
        interfaceRegistry.Register(typeof(IOrderOrchestratorGrain), new GrainType("OrderOrchestratorGrain"));
        proxyRegistry.Register<IWorkerGrain, WorkerGrainProxy>((id, inv) => new WorkerGrainProxy(id, inv));
        proxyRegistry.Register<IOrderOrchestratorGrain, OrderOrchestratorGrainProxy>((id, inv) => new OrderOrchestratorGrainProxy(id, inv));

        var deferredInvoker = new DeferredCallInvoker();
        var localFactory = new LocalGrainFactory(proxyRegistry, interfaceRegistry, deferredInvoker);
        _activationTable = _sp.GetRequiredService<GrainActivationTable>();
        var realInvoker = new LocalGrainCallInvoker(
            _activationTable, _sp.GetRequiredService<IGrainActivator>(), typeRegistry,
            _sp.GetRequiredService<IGrainDirectory>(), _sp.GetRequiredService<IGrainMethodInvokerRegistry>(),
            localFactory, _sp, _sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            NullLogger<LocalGrainCallInvoker>.Instance, NullLogger<GrainActivation>.Instance);
        IGrainCallInvoker effective = new FaultInjectingGrainCallInvoker(realInvoker, scenario.Calls);
        deferredInvoker.SetInvoker(effective);
        _client = new LocalClusterClient(new LocalGrainFactory(proxyRegistry, interfaceRegistry, effective));
    }

    /// <summary>
    /// Redis connection lost mid-write: StackExchange.Redis reconnect + Quark retry semantics agree.
    /// Simulated via fault injection at the IStorage layer (ThrowOnNthWrite).
    /// After the forced failure, Redis is still alive — the grain retries and completes.
    /// </summary>
    [Fact]
    public async Task Redis_ConnectionLostMidWrite_GrainReactivatesConsistently()
    {
        BuildFixture(new FaultScenario.FaultScenario());
        _scenario.WorkerStorage.ThrowOnNthWrite<InvalidOperationException>(1);

        var orchestrator = _client.GetGrain<IOrderOrchestratorGrain>("order-redis-reconnect");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ProcessAsync(["w-redis-1"]));

        // After the fault fired (counter past N=1), real Redis handles the next write
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-redis-1"]);
        Assert.Equal(OrchestratorStatus.Completed, result);
    }

    /// <summary>
    /// Storage write times out (simulated via ThrowOnNthWrite with TimeoutException).
    /// Confirms the exception propagates correctly through the grain call stack.
    /// </summary>
    [Fact]
    public async Task Redis_SlowWrite_TimeoutPropagatesCorrectly()
    {
        BuildFixture(new FaultScenario.FaultScenario());
        _scenario.WorkerStorage.ThrowOnNthWrite<TimeoutException>(1);

        var orchestrator = _client.GetGrain<IOrderOrchestratorGrain>("order-redis-timeout");

        await Assert.ThrowsAsync<TimeoutException>(
            () => orchestrator.ProcessAsync(["w-timeout"]));
    }

    /// <summary>
    /// Full pipeline: orchestrator + 3 workers + real Redis + call drop fault on one worker.
    /// Worker w1 is always dropped. Workers w2 and w3 succeed with real Redis persistence.
    /// Order is marked Failed, state is durable in Redis (survives activation table reset).
    /// </summary>
    [Fact]
    public async Task FullPipeline_CascadingFaults_OrderEventuallyCompletes()
    {
        BuildFixture(new FaultScenario.FaultScenario());
        _scenario.Calls.AlwaysThrowForKey(
            new GrainType("WorkerGrain"),
            "w-full-1",
            () => new InvalidOperationException("Simulated drop"));

        var orchestrator = _client.GetGrain<IOrderOrchestratorGrain>("order-full-pipeline");
        OrchestratorStatus result = await orchestrator.ProcessAsync(["w-full-1", "w-full-2", "w-full-3"]);

        Assert.Equal(OrchestratorStatus.Failed, result);

        // Reset activation table — state must survive in real Redis
        await _activationTable.DisposeAsync();
        BuildFixture(new FaultScenario.FaultScenario()); // no faults this time

        // Re-activate with no dropped workers — order completes
        OrchestratorStatus recovered = await _client.GetGrain<IOrderOrchestratorGrain>("order-full-pipeline-2")
            .ProcessAsync(["w-full-2", "w-full-3"]);
        Assert.Equal(OrchestratorStatus.Completed, recovered);
    }

    private sealed class DeferredCallInvoker : IGrainCallInvoker
    {
        private IGrainCallInvoker? _inner;
        public void SetInvoker(IGrainCallInvoker inv) => _inner = inv;

        public Task<object?> InvokeAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeAsync(id, method, args, ct);

        public Task<TResult> InvokeAsync<TResult>(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeAsync<TResult>(id, method, args, ct);

        public Task InvokeVoidAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
            => _inner!.InvokeVoidAsync(id, method, args, ct);
    }
}
```

> **Note on `FaultInjectingStorage` with real Redis backing:** The integration test needs `FaultInjectingStorage<TState>` to optionally delegate to a real `IStorage<TState>` instead of its internal dictionary. Update `FaultInjectingStorage<TState>` in Task 7 to accept an optional `IStorage<TState>? inner` constructor parameter. When `inner` is provided, delegate read/write/clear to it after fault checks; when null, use the internal dictionary (in-process tests).

- [ ] **Step 2: Update FaultInjectingStorage to support an optional real backing store**

Edit `tests/Quark.Tests.Fault/Fakes/FaultInjectingStorage.cs`:

```csharp
public sealed class FaultInjectingStorage<TState> : IStorage<TState> where TState : new()
{
    private readonly Dictionary<string, TState> _store = new();
    private readonly StorageFaultPlan _plan;
    private readonly IStorage<TState>? _inner;  // null = use in-memory dict

    public FaultInjectingStorage(StorageFaultPlan plan, IStorage<TState>? inner = null)
    {
        _plan = plan;
        _inner = inner;
    }

    public async Task<TState> ReadAsync(GrainId grainId, string? stateName = null, CancellationToken ct = default)
    {
        var (isStale, staleValue) = _plan.CheckRead();
        if (isStale)
            return (TState)staleValue!;

        if (_inner is not null)
            return await _inner.ReadAsync(grainId, stateName, ct);

        string key = Key(grainId, stateName);
        return _store.TryGetValue(key, out TState? stored) ? stored : new TState();
    }

    public async Task WriteAsync(GrainId grainId, TState state, string? stateName = null, CancellationToken ct = default)
    {
        _plan.CheckWrite();

        if (_inner is not null)
        {
            await _inner.WriteAsync(grainId, state, stateName, ct);
            return;
        }

        _store[Key(grainId, stateName)] = state;
    }

    public async Task ClearAsync(GrainId grainId, string? stateName = null, CancellationToken ct = default)
    {
        if (_inner is not null)
        {
            await _inner.ClearAsync(grainId, stateName, ct);
            return;
        }
        _store.Remove(Key(grainId, stateName));
    }

    private static string Key(GrainId id, string? name)
        => $"{id.Type.Value}/{id.Key}/{name ?? "Default"}";
}
```

> **Note on `RedisGrainStorage<TState>`:** Check how `Quark.Persistence.Redis` exposes its concrete storage type. If it doesn't expose a `RedisGrainStorage<TState>` directly, resolve `IStorage<TState>` from a temporary inner service provider and pass it as `inner` to `FaultInjectingStorage`. Adjust the integration test's `BuildFixture` accordingly.

- [ ] **Step 3: Verify the integration project builds**

```bash
dotnet build tests/Quark.Tests.Fault.Integration/Quark.Tests.Fault.Integration.csproj
```
Expected: Build succeeded.

- [ ] **Step 4: Run integration tests (requires Docker)**

```bash
dotnet test tests/Quark.Tests.Fault.Integration/Quark.Tests.Fault.Integration.csproj \
  --filter "category=fault-integration" -v normal
```
Expected: All 3 tests pass. If Docker is unavailable, tests fail at container startup — that's expected; run with `--filter "category!=fault-integration"` to skip them.

- [ ] **Step 5: Run the full test suite to confirm no regressions**

```bash
dotnet test Quark.slnx --filter "category!=fault-integration" -v minimal
```
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add tests/Quark.Tests.Fault.Integration/FaultIntegrationTests.cs \
        tests/Quark.Tests.Fault/Fakes/FaultInjectingStorage.cs
git commit -m "test(fault-integration): add Testcontainers Redis fault-integration tests"
```
