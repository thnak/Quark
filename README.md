# Orleans Repository Architecture Guide

## What this repository is

This repository contains the core Microsoft Orleans framework: a distributed virtual actor platform for .NET.

At a high level:

- **Grains** are the primary programming model (stateful or stateless virtual actors).
- **Silos** host and execute grains.
- **Clients** call grains from external applications.
- **Providers/extensions** plug in storage, clustering, streaming, reminders, and infrastructure integrations.

## Repository layout

### `src/`
Core framework code and extension packages.

- **Core runtime and SDK**
  - `Orleans.Core.Abstractions` – foundational contracts and abstractions.
  - `Orleans.Core` – shared runtime/client core components.
  - `Orleans.Runtime` – silo-side runtime implementation.
  - `Orleans.Client` – client-side communication stack.
  - `Orleans.Server` – server metapackage for silo hosting.
  - `Orleans.Sdk` – build-time/codegen SDK integration.
  - `Orleans.CodeGenerator`, `Orleans.Analyzers` – source generation/analyzers.
- **Serialization**
  - `Orleans.Serialization` + abstractions.
  - Optional serializer integrations (System.Text.Json, Newtonsoft.Json, MessagePack, MemoryPack, Protobuf, F#).
- **Feature packages**
  - Streaming, reminders, event sourcing, durable jobs, transactions, testing host.
- **Infrastructure extensions**
  - Provider-specific folders: `AdoNet`, `AWS`, `Azure`, `Cassandra`, `Redis`, `Dashboard`, and more.

### `test/`
Unit, integration, analyzer, serialization, and distributed test projects that mirror major runtime areas.

### `samples/`
Entry point for official Orleans sample applications (now hosted in `dotnet/samples`).

### `playground/`
Experimental and scenario-focused apps for trying architecture/runtime behaviors.

### `src/api/`
Generated API surface baselines for packable projects.

## Key technologies used

- **Language/runtime:** C#, .NET (SDK pinned in `global.json`).
- **Build system:** `dotnet build` with solution file `Orleans.slnx`.
- **Dependency management:** central package management via `Directory.Packages.props`.
- **Testing:** `dotnet test`, xUnit-based test projects, plus distributed performance/scenario tests.
- **Integrations:** Azure, AWS, Redis, ADO.NET, Cassandra, Kubernetes, OpenTelemetry, multiple serializers.

## How code is organized conceptually

### Layered architecture

1. **Programming model layer**
   - Grain interfaces/classes, grain identity, state, lifecycle APIs.
2. **Runtime layer**
   - Activation/deactivation, placement, messaging, scheduling, clustering.
3. **Infrastructure layer**
   - Persistence providers, membership stores, stream providers, reminder stores, dashboards.
4. **Tooling layer**
   - Source generators, analyzers, SDK/build support.

### Package composition model

- Start with Orleans core/client/server packages.
- Add provider packages based on deployment/storage needs.
- Add optional feature packages (transactions, streaming, durable jobs, etc.) as required.

## Architecture usage patterns

### 1) Local development architecture

- Single silo + co-hosted client or separate local client.
- In-memory/dev providers where possible.
- Fastest feedback loop for grain behavior and API evolution.

### 2) Production cluster architecture

- Multiple silos in a cluster.
- External membership and persistence providers (for resiliency).
- Health checks, telemetry, and rolling deployment/versioning strategies.

### 3) Cloud-native architecture

- Orleans hosted in containers/Kubernetes.
- Cloud provider integrations for clustering, persistence, streams, and reminders.
- Scales horizontally by adding silos.

### 4) Event-driven architecture

- Orleans Streams for pub/sub and near real-time processing.
- Grains as stateful processors and workflow coordinators.
- Optional durable jobs/event sourcing for long-running or replayable workflows.

## Choosing packages by need

- Need only grain calls from an app: **Client** packages.
- Need to host grains: **Server/Runtime** packages.
- Need persistence/reminders/clustering: add provider package(s) from `src/Azure`, `src/AWS`, `src/Redis`, `src/AdoNet`, etc.
- Need custom serialization: add an `Orleans.Serialization.*` integration package.
- Need Kubernetes hosting support: use `Orleans.Hosting.Kubernetes`.

## Native AOT and Linker-Trimming

Orleans targets full Native AOT support as a long-term goal. For an in-depth audit of the current state and a step-by-step roadmap, see **[docs/aot.md](docs/aot.md)**.

### Design constraints for new code

1. **Prefer code generation over reflection.** The Orleans source generator emits all grain-proxy, serializer, copier, and activator code at build time. New features should integrate with the code generator rather than resolving types or members at runtime.

2. **Annotate unavoidable reflection.** Any code that must use runtime reflection, `Assembly.Load`, `DynamicMethod`, or similar must be annotated with `[RequiresUnreferencedCode]` and/or `[RequiresDynamicCode]` and the message must explain what the caller should do instead.

3. **Guard JIT-only paths with `RuntimeFeature.IsDynamicCodeSupported`.** Where a fast JIT path exists alongside a slower but AOT-safe fallback, use `RuntimeFeature.IsDynamicCodeSupported` to select the path at runtime. The AOT compiler will eliminate the JIT-only branch as dead code.

4. **Use `[UnsafeAccessor]` for private-member access on .NET 8+.** The `[System.Runtime.CompilerServices.UnsafeAccessor]` attribute is fully AOT-compatible and supersedes `DynamicMethod`-based field/method accessors for private members.

5. **Do not add new `ISerializable`-dependent code.** The `ISerializable` pattern requires `DynamicMethod` and is incompatible with Native AOT. New exception types and serializable classes should use `[GenerateSerializer]` instead.

6. **Register providers explicitly.** Auto-discovery via `RegisterProviderAttribute` assembly scanning is not trim-safe. New providers should offer explicit registration extension methods in addition to (or instead of) the attribute-based discovery path.

## Start points for contributors

- Read root `README.md` for model and features.
- Explore `src/Orleans.Core*`, `src/Orleans.Runtime`, and `src/Orleans.Client` first.
- Follow with provider folders relevant to your target deployment.
- Use tests in `test/` matching your changed component for validation.
