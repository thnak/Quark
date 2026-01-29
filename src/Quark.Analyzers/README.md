# Quark.Analyzers

Roslyn analyzers and code fix providers for Quark actor framework development.

## Overview

This package provides compile-time diagnostics and automatic code fixes to help developers follow best practices when building Quark actor systems. The analyzers ensure:

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

// ✅ Correct
[Actor]
public class MyActor : ActorBase
{
    public async Task ProcessDataAsync()
    {
        await Task.CompletedTask;
    }
}
```

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
- Interfaces (except common collections)
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

## Code Fix Providers

### ActorMethodSignatureCodeFixProvider
Provides automatic fixes for QUARK004:
- **Convert to async Task**: Converts synchronous methods to return `Task`
- **Convert to async ValueTask**: Converts synchronous methods to return `ValueTask` (offered for void/value types)

### MissingActorAttributeCodeFixProvider
Provides automatic fixes for QUARK005:
- **Add [Actor] attribute**: Adds the missing attribute to actor classes
- Automatically includes the necessary `using Quark.Abstractions;` directive

## Usage

The analyzers are automatically enabled when you reference the `Quark.Analyzers` project:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Quark.Analyzers/Quark.Analyzers.csproj" 
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
