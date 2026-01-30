# Quark.Analyzers

Roslyn analyzers for Quark actor framework development.

## Project Structure

This project is split into two assemblies to comply with **RS1038** (compiler extensions should not reference assemblies unavailable in all compilation scenarios):

- **Quark.Analyzers**: Contains diagnostic analyzers only (no Workspaces dependency)
- **Quark.Analyzers.CodeFixes**: Contains code fix providers (separate project with Workspaces dependency)

See [Quark.Analyzers.CodeFixes README](../Quark.Analyzers.CodeFixes/README.md) for details on code fix providers.

## Overview

This package provides compile-time diagnostics to help developers follow best practices when building Quark actor systems. The analyzers ensure:

- Proper async/await patterns in actor methods
- Correct use of attributes for actor registration
- Serializable parameters for distributed calls
- Valid stream configurations

## Diagnostic Rules

### QUARK001: Invalid stream namespace format
**Severity:** Warning  
**Category:** Quark.Streams

Stream namespace should follow the format 'category/subcategory' (e.g., 'orders/processed').

### QUARK002: Missing IStreamConsumer interface
**Severity:** Error  
**Category:** Quark.Streams

Actor with `[QuarkStream]` attribute must implement `IStreamConsumer<T>` to receive messages.

### QUARK003: Duplicate stream subscription
**Severity:** Warning  
**Category:** Quark.Streams

Actor has multiple `[QuarkStream]` attributes with the same namespace.

### QUARK004: Actor method should be async
**Severity:** Warning  
**Category:** Quark.Actors

Actor methods should return `Task`, `ValueTask`, `Task<T>`, or `ValueTask<T>` instead of synchronous return types to support distributed calls and maintain responsiveness.

**Code Fix Available:** ✅  
- Convert to async Task
- Convert to async ValueTask (for void or value types)

**Example:**
```csharp
// ❌ Will trigger QUARK004
[Actor]
public class MyActor : ActorBase
{
    public void ProcessData() // Synchronous method
    {
        // ...
    }
}

// ✅ Correct (after applying code fix, method name should be updated manually)
[Actor]
public class MyActor : ActorBase
{
    public async Task ProcessData() // Code fix adds async and Task, developer should rename to ProcessDataAsync
    {
        await Task.CompletedTask;
    }
}
```

**Note:** The code fix converts the return type and adds the `async` modifier but does not rename the method. Developers should manually rename methods to follow the `Async` suffix convention (e.g., `ProcessData` → `ProcessDataAsync`).

### QUARK005: Actor class missing [Actor] attribute
**Severity:** Warning  
**Category:** Quark.Actors

Classes inheriting from `ActorBase` should have the `[Actor]` attribute for proper factory registration and source generation.

**Code Fix Available:** ✅  
- Add `[Actor]` attribute to class
- Automatically adds `using Quark.Abstractions;` if missing

**Example:**
```csharp
// ❌ Will trigger QUARK005
public class MyActor : ActorBase // Missing [Actor] attribute
{
    public MyActor(string actorId) : base(actorId) { }
}

// ✅ Correct
[Actor]
public class MyActor : ActorBase
{
    public MyActor(string actorId) : base(actorId) { }
}
```

### QUARK006: Actor method parameter may not be serializable
**Severity:** Warning  
**Category:** Quark.Actors

Actor method parameters should use types that can be JSON serialized for distributed calls. Avoid:
- Delegates (Action, Func)
- Interfaces (except `System.Collections.Generic` interfaces like `IList<T>`, `IDictionary<K,V>`)
- Complex types without proper serialization support

**Example:**
```csharp
// ❌ Will trigger QUARK006
[Actor]
public class MyActor : ActorBase
{
    public async Task ProcessAsync(Action callback) // Delegate not serializable
    {
        await Task.CompletedTask;
    }
}

// ✅ Correct - Use serializable types
[Actor]
public class MyActor : ActorBase
{
    public async Task ProcessAsync(string message, List<int> items)
    {
        await Task.CompletedTask;
    }
}
```

### QUARK007: Potential reentrancy issue detected
**Severity:** Warning  
**Category:** Quark.Actors

Actor methods calling other methods on the same actor instance (e.g., `this.MethodAsync()`) can cause reentrancy issues in non-reentrant actors. This can lead to deadlocks or unexpected behavior.

This warning is only raised if the actor is marked with `[Actor(Reentrant = false)]` or does not explicitly set `Reentrant = true` (default is non-reentrant).

**Example:**
```csharp
// ❌ Will trigger QUARK007
[Actor(Reentrant = false)]
public class MyActor : ActorBase
{
    public async Task OuterMethodAsync()
    {
        await this.InnerMethodAsync(); // QUARK007: Potential reentrancy
    }

    public async Task InnerMethodAsync()
    {
        await Task.CompletedTask;
    }
}

// ✅ Option 1: Mark actor as reentrant
[Actor(Reentrant = true)]
public class MyActor : ActorBase
{
    public async Task OuterMethodAsync()
    {
        await this.InnerMethodAsync(); // OK - actor allows reentrancy
    }

    public async Task InnerMethodAsync()
    {
        await Task.CompletedTask;
    }
}

// ✅ Option 2: Restructure to avoid self-calls
[Actor(Reentrant = false)]
public class MyActor : ActorBase
{
    public async Task ProcessAsync()
    {
        // Inline the logic instead of calling another method
        await Task.CompletedTask;
    }
}
```

### QUARK008: Blocking call detected in actor method
**Severity:** Warning  
**Category:** Quark.Performance

Actor methods should avoid blocking calls that can cause thread starvation and deadlocks. Blocking operations include:
- `Thread.Sleep`
- `Task.Wait`, `Task.WaitAll`, `Task.WaitAny`
- `Task.Result`
- `.GetAwaiter().GetResult()`
- `Monitor.Enter`, `Monitor.Wait`
- `Semaphore.WaitOne`, `Mutex.WaitOne`

Use `await` with async APIs instead to maintain responsiveness.

**Example:**
```csharp
// ❌ Will trigger QUARK008
[Actor]
public class MyActor : ActorBase
{
    public async Task ProcessAsync()
    {
        Thread.Sleep(1000); // QUARK008: Blocking call
        var result = SomeTaskAsync().Result; // QUARK008: Blocking call
        await Task.CompletedTask;
    }
}

// ✅ Correct - Use async/await
[Actor]
public class MyActor : ActorBase
{
    public async Task ProcessAsync()
    {
        await Task.Delay(1000);
        var result = await SomeTaskAsync();
    }
}
```

### QUARK009: Synchronous I/O detected in actor method
**Severity:** Warning  
**Category:** Quark.Performance

Synchronous file I/O methods block threads and reduce scalability. Use async alternatives for better performance:
- `File.ReadAllText` → `File.ReadAllTextAsync`
- `File.WriteAllText` → `File.WriteAllTextAsync`
- `File.ReadAllLines` → `File.ReadAllLinesAsync`
- `File.WriteAllLines` → `File.WriteAllLinesAsync`
- `File.ReadAllBytes` → `File.ReadAllBytesAsync`
- `File.WriteAllBytes` → `File.WriteAllBytesAsync`

**Example:**
```csharp
// ❌ Will trigger QUARK009
[Actor]
public class MyActor : ActorBase
{
    public async Task LoadDataAsync(string filePath)
    {
        var content = File.ReadAllText(filePath); // QUARK009: Synchronous I/O
        await Task.CompletedTask;
    }
}

// ✅ Correct - Use async file I/O
[Actor]
public class MyActor : ActorBase
{
    public async Task LoadDataAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
    }
}
```

## Code Fix Providers

### ActorMethodSignatureCodeFixProvider
Provides automatic fixes for QUARK004:
- **Convert to async Task**: Converts synchronous methods to return `Task`
- **Convert to async ValueTask**: Converts synchronous methods to return `ValueTask` (offered for void/value types)

### MissingActorAttributeCodeFixProvider
Provides automatic fixes for QUARK005:
- **Add [Actor] attribute**: Adds the missing attribute to actor classes
- Automatically includes the necessary `using Quark.Abstractions;` directive

### StatePropertyCodeFixProvider
Provides code generation for actor state management:
- **Add QuarkState property (string)**: Generates a string state property with `[QuarkState]` attribute
- **Add QuarkState property (int)**: Generates an integer state property with `[QuarkState]` attribute
- **Add QuarkState property (custom type)**: Generates a custom state object property

This code fix is available as a refactoring action (not triggered by a diagnostic). Use the lightbulb menu on any actor class to add state properties.

**Example:**
```csharp
// Before
[Actor]
public class MyActor : ActorBase
{
    public MyActor(string actorId) : base(actorId) { }
}

// After applying "Add QuarkState property (string)"
[Actor]
public class MyActor : ActorBase
{
    public MyActor(string actorId) : base(actorId) { }

    /// <summary>
    /// Persisted state for this actor.
    /// </summary>
    [QuarkState]
    public string State { get; set; }
}
```

### SupervisionScaffoldCodeFixProvider
Provides scaffolding for supervision hierarchy implementations:
- **Implement ISupervisor (restart on failure)**: Generates `ISupervisor` implementation that restarts failed child actors
- **Implement ISupervisor (stop on failure)**: Generates `ISupervisor` implementation that stops failed child actors
- **Implement ISupervisor (custom strategy)**: Generates `ISupervisor` implementation with exception-based decision logic

This code fix is available as a refactoring action on any actor class.

**Example:**
```csharp
// Before
[Actor]
public class MyActor : ActorBase
{
    public MyActor(string actorId) : base(actorId) { }
}

// After applying "Implement ISupervisor (custom strategy)"
[Actor]
public class MyActor : ActorBase, ISupervisor
{
    public MyActor(string actorId) : base(actorId) { }

    /// <summary>
    /// Handles child actor failures.
    /// </summary>
    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Custom supervision logic based on exception type
        return context.Exception switch
        {
            TimeoutException => Task.FromResult(SupervisionDirective.Resume),
            InvalidOperationException => Task.FromResult(SupervisionDirective.Restart),
            OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
            _ => Task.FromResult(SupervisionDirective.Escalate)
        };
    }
}
```

## Usage

The analyzers are automatically enabled when you reference both projects:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Quark.Analyzers/Quark.Analyzers.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
  <ProjectReference Include="path/to/Quark.Analyzers.CodeFixes/Quark.Analyzers.CodeFixes.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Warnings appear in:
- Visual Studio Error List
- Visual Studio Code Problems panel
- Command line build output
- CI/CD pipeline logs

Code fixes are available through:
- **Visual Studio**: Click the lightbulb icon or press `Ctrl+.` on the warning
- **Visual Studio Code**: Click the lightbulb or use `Ctrl+.` (Windows/Linux) or `Cmd+.` (macOS)
- **JetBrains Rider**: Use `Alt+Enter` on the warning

## Configuration

To suppress specific warnings, use standard .NET suppression mechanisms:

```csharp
#pragma warning disable QUARK004
public void LegacyMethod() { }
#pragma warning restore QUARK004
```

Or in `.editorconfig`:
```ini
[*.cs]
dotnet_diagnostic.QUARK004.severity = suggestion
```

## AOT Compatibility

All analyzers are fully compatible with Native AOT compilation and use zero reflection at runtime.

## See Also

- [Quark Framework Documentation](../../README.md)
- [Source Generator Setup Guide](../../docs/SOURCE_GENERATOR_SETUP.md)
- [Enhancements Roadmap](../../docs/ENHANCEMENTS.md)
