# IQuarkActor Inheritance Analyzer

## Overview

The `QuarkActorInheritanceAnalyzer` is a Roslyn analyzer that detects problematic inheritance patterns in actor interfaces and implementations. It helps prevent common issues that can lead to actor resolution troubles and performance problems in distributed Quark clusters.

## Diagnostic Rules

### QUARK010: Multiple Implementations of Same IQuarkActor Interface

**Severity**: Warning  
**Category**: Quark.Actors

**Description**: Detects when the same `IQuarkActor` interface is implemented by multiple concrete classes. This can cause ambiguity in actor routing and resolution.

**Problem**:
```csharp
public interface ICounterActor : IQuarkActor
{
    void Increment();
}

[Actor]
public class CounterActorV1 : ActorBase, ICounterActor  // ⚠️ QUARK010
{
    // Implementation
}

[Actor]
public class CounterActorV2 : ActorBase, ICounterActor  // ⚠️ QUARK010
{
    // Different implementation
}
```

**Why This is Problematic**:

1. **Routing Ambiguity**: When a client calls `GetActor<ICounterActor>("id")`, which implementation should be used?
2. **Inconsistent Behavior**: Different silos might resolve to different implementations, causing inconsistent actor behavior
3. **Testing Difficulty**: Hard to reason about which version will be activated
4. **Proxy Generation**: The source generator may produce ambiguous proxy code

**Solution**:

Choose one of these patterns:

1. **Single Implementation** (Recommended):
```csharp
public interface ICounterActor : IQuarkActor
{
    void Increment();
}

[Actor]
public class CounterActor : ActorBase, ICounterActor
{
    // Single implementation
}
```

2. **Use Different Interfaces**:
```csharp
public interface ICounterActorV1 : IQuarkActor
{
    void Increment();
}

public interface ICounterActorV2 : IQuarkActor
{
    void Increment();
    void IncrementBy(int amount);  // Added functionality
}

[Actor]
public class CounterActorV1 : ActorBase, ICounterActorV1 { }

[Actor]
public class CounterActorV2 : ActorBase, ICounterActorV2 { }
```

3. **Abstract Base + Concrete Implementations**:
```csharp
public interface ICounterActor : IQuarkActor
{
    void Increment();
}

public abstract class CounterActorBase : ActorBase, ICounterActor
{
    // Common functionality
    public abstract void Increment();
}

[Actor]
public class InMemoryCounterActor : CounterActorBase
{
    public override void Increment() { /* In-memory */ }
}

// Only one is registered as concrete actor at runtime
```

---

### QUARK011: Deep Inheritance Chain

**Severity**: Info  
**Category**: Quark.Actors

**Description**: Detects when an `IQuarkActor` implementation has a deep inheritance chain (depth > 3). Deep inheritance can impact performance and make actor resolution more complex.

**Problem**:
```csharp
public interface IMyActor : IQuarkActor { }

public abstract class BaseActorLayer1 : ActorBase { }
public abstract class BaseActorLayer2 : BaseActorLayer1 { }
public abstract class BaseActorLayer3 : BaseActorLayer2 { }

[Actor]
public class MyActor : BaseActorLayer3, IMyActor  // ℹ️ QUARK011 (depth = 4)
{
    // Implementation
}
```

**Why This is Problematic**:

1. **Virtual Call Overhead**: Each layer adds virtual method call overhead
2. **Complexity**: Harder to understand actor behavior
3. **Serialization**: More complex state serialization if using `StatefulActorBase`
4. **AOT Compilation**: Deeper chains may affect AOT compilation size and performance

**Acceptable Depth**:

- **Depth 1**: `ActorBase` → `MyActor` ✅
- **Depth 2**: `ActorBase` → `CustomBase` → `MyActor` ✅
- **Depth 3**: `ActorBase` → `CustomBase` → `SpecializedBase` → `MyActor` ✅ (acceptable)
- **Depth 4+**: `ActorBase` → ... → `MyActor` ⚠️ (consider refactoring)

**Solution**:

Use composition instead of deep inheritance:

**Before** (deep inheritance):
```csharp
public abstract class BaseActorLayer1 : ActorBase
{
    protected void CommonMethod1() { }
}

public abstract class BaseActorLayer2 : BaseActorLayer1
{
    protected void CommonMethod2() { }
}

public abstract class BaseActorLayer3 : BaseActorLayer2
{
    protected void CommonMethod3() { }
}

[Actor]
public class MyActor : BaseActorLayer3, IMyActor { }
```

**After** (composition):
```csharp
public interface IActorBehavior1
{
    void CommonMethod1();
}

public interface IActorBehavior2
{
    void CommonMethod2();
}

public interface IActorBehavior3
{
    void CommonMethod3();
}

[Actor]
public class MyActor : ActorBase, IMyActor
{
    private readonly IActorBehavior1 _behavior1;
    private readonly IActorBehavior2 _behavior2;
    private readonly IActorBehavior3 _behavior3;

    public MyActor(string actorId, IActorBehavior1 b1, IActorBehavior2 b2, IActorBehavior3 b3) 
        : base(actorId)
    {
        _behavior1 = b1;
        _behavior2 = b2;
        _behavior3 = b3;
    }
    
    // Delegate to composed behaviors
}
```

## Configuration

### Suppressing Warnings

If you have a valid reason for multiple implementations or deep inheritance, you can suppress the warning:

```csharp
#pragma warning disable QUARK010
[Actor]
public class CounterActorV1 : ActorBase, ICounterActor { }

[Actor]
public class CounterActorV2 : ActorBase, ICounterActor { }
#pragma warning restore QUARK010
```

Or in a `.editorconfig` file:

```ini
[*.cs]
dotnet_diagnostic.QUARK010.severity = none
dotnet_diagnostic.QUARK011.severity = none
```

### Disabling Rules

To disable the analyzer entirely, add to your `.csproj`:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);QUARK010;QUARK011</NoWarn>
</PropertyGroup>
```

## Best Practices

1. **One Interface, One Implementation**: Keep a 1:1 mapping between actor interfaces and implementations
2. **Shallow Hierarchies**: Prefer composition over deep inheritance
3. **Abstract Classes**: Use abstract classes for shared behavior, but keep the chain shallow
4. **Versioning**: When evolving actors, create new interfaces (e.g., `ICounterActorV2`) rather than multiple implementations
5. **Testing**: If you need multiple implementations for testing, use mock frameworks or create test-specific interfaces

## See Also

- [Actor Design Patterns](./ACTOR_DESIGN_PATTERNS.md)
- [Source Generator Setup](./SOURCE_GENERATOR_SETUP.md)
- [Quark Analyzers README](../src/Quark.Analyzers/README.md)
