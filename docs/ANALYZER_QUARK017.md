# QUARK017: Unsupported Parameter Type Analyzer

## Overview

The `UnsupportedParameterTypeAnalyzer` (QUARK017) is a Roslyn analyzer that enforces type constraints on actor interface method parameters to ensure compatibility with the binary converter serialization system.

## Purpose

With Quark's binary converter system, only concrete, serializable types can be used as parameters in actor interface methods. This analyzer prevents the use of:

- **Lazy evaluation types** that cannot be reliably serialized
- **Delegates** that cannot be transferred across process boundaries  
- **Expression trees** that require reflection to evaluate
- **General interfaces** that lack concrete serialization information

## Diagnostic Information

**Diagnostic ID**: `QUARK017`  
**Category**: `Quark.Actors`  
**Severity**: **Error** (prevents compilation)  
**Default**: Enabled

## Unsupported Types

### 1. Delegates
```csharp
// ❌ ERROR QUARK017
public interface IMyActor : IQuarkActor
{
    Task ProcessAsync(Action callback);              // Delegate
    Task<int> ComputeAsync(Func<int> calculator);    // Delegate
    Task HandleAsync(CustomEventHandler handler);     // Custom delegate
}
```

**Why?** Delegates reference methods in memory and cannot be serialized across process boundaries.

### 2. Expression Trees
```csharp
// ❌ ERROR QUARK017
public interface IQueryActor : IQuarkActor
{
    Task<List<T>> QueryAsync<T>(Expression<Func<T, bool>> predicate);
}
```

**Why?** Expression trees require reflection to evaluate and cannot be reliably serialized.

### 3. Lazy Evaluation Types
```csharp
// ❌ ERROR QUARK017
public interface IDataActor : IQuarkActor
{
    Task ProcessAsync(IEnumerable<int> items);         // Lazy enumerable
    Task StreamAsync(IAsyncEnumerable<int> items);     // Async lazy enumerable
}
```

**Why?** Lazy evaluation types don't materialize their data until enumerated, making them unsuitable for serialization. The data source may not be available after deserialization.

### 4. General Interface Types
```csharp
// ❌ ERROR QUARK017
public interface IProcessorActor : IQuarkActor
{
    Task ProcessAsync(IService service);  // General interface
}
```

**Why?** Interfaces lack concrete type information needed for serialization. The actual implementation type is unknown at compile time.

**Exception**: Common collection interfaces are allowed:
- `IList<T>`, `ICollection<T>`, `IDictionary<K,V>`, `ISet<T>`
- `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IReadOnlyDictionary<K,V>`

## Supported Types

### 1. Primitives
```csharp
// ✅ ALLOWED
public interface ICalculatorActor : IQuarkActor
{
    Task<int> AddAsync(int a, int b);
    Task<string> FormatAsync(double value);
    Task<bool> ValidateAsync(string input);
}
```

### 2. Concrete Classes
```csharp
// ✅ ALLOWED
public interface IOrderActor : IQuarkActor
{
    [BinaryConverter(typeof(OrderConverter), ParameterName = "order")]
    Task ProcessAsync(Order order);
}
```

### 3. Structs and Records
```csharp
// ✅ ALLOWED
public interface IPointActor : IQuarkActor
{
    [BinaryConverter(typeof(Point3DConverter), ParameterName = "point")]
    Task MoveAsync(Point3D point);
    
    [BinaryConverter(typeof(ConfigConverter), ParameterName = "config")]
    Task ConfigureAsync(ConfigRecord config);
}

public struct Point3D { public double X, Y, Z; }
public record ConfigRecord(string Name, int Value);
```

### 4. Concrete Collections
```csharp
// ✅ ALLOWED
public interface IListActor : IQuarkActor
{
    [BinaryConverter(typeof(ListIntConverter), ParameterName = "items")]
    Task ProcessAsync(List<int> items);
    
    [BinaryConverter(typeof(IntArrayConverter), ParameterName = "values")]
    Task AnalyzeAsync(int[] values);
    
    [BinaryConverter(typeof(DictionaryConverter), ParameterName = "map")]
    Task StoreAsync(Dictionary<string, int> map);
}
```

### 5. Common Framework Types
```csharp
// ✅ ALLOWED
public interface ISchedulerActor : IQuarkActor
{
    [BinaryConverter(typeof(DateTimeConverter), ParameterName = "timestamp")]
    Task ScheduleAsync(DateTime timestamp);
    
    [BinaryConverter(typeof(GuidConverter), ParameterName = "id")]
    Task ProcessAsync(Guid id);
    
    [BinaryConverter(typeof(TimeSpanConverter), ParameterName = "duration")]
    Task DelayAsync(TimeSpan duration);
}
```

## Error Message

When the analyzer detects an unsupported type, it produces an error like:

```
Error QUARK017: Parameter 'callback' of type 'System.Action' in actor method 'ProcessAsync' is not supported. 
Actor interfaces only support concrete types (class, struct, record, primitives, arrays), not delegates (Action, Func), 
expression trees (Expression<T>), or lazy types (IEnumerable, IAsyncEnumerable). 
Consider using a concrete collection type like List<T> or T[] instead.
```

## How to Fix

### Problem: Using IEnumerable
```csharp
// ❌ ERROR
Task ProcessAsync(IEnumerable<int> items);
```

**Solutions:**
```csharp
// ✅ Use List<T>
[BinaryConverter(typeof(ListIntConverter), ParameterName = "items")]
Task ProcessAsync(List<int> items);

// ✅ Use array
[BinaryConverter(typeof(IntArrayConverter), ParameterName = "items")]
Task ProcessAsync(int[] items);
```

### Problem: Using Action/Func
```csharp
// ❌ ERROR
Task RegisterCallbackAsync(Action<string> callback);
```

**Solutions:**
```csharp
// ✅ Use command pattern with concrete type
[BinaryConverter(typeof(CallbackCommandConverter), ParameterName = "command")]
Task RegisterCallbackAsync(CallbackCommand command);

public record CallbackCommand(string TargetActorId, string MethodName);

// ✅ Or use event subscription pattern
Task SubscribeAsync(string eventName);
```

### Problem: Using Expression<T>
```csharp
// ❌ ERROR
Task<List<Order>> QueryAsync(Expression<Func<Order, bool>> predicate);
```

**Solutions:**
```csharp
// ✅ Use query object pattern
[BinaryConverter(typeof(OrderQueryConverter), ParameterName = "query")]
Task<List<Order>> QueryAsync(OrderQuery query);

public record OrderQuery(
    string? CustomerId = null,
    decimal? MinAmount = null,
    DateTime? AfterDate = null
);
```

### Problem: Using Interface
```csharp
// ❌ ERROR
Task ProcessAsync(IDataService service);
```

**Solutions:**
```csharp
// ✅ Use concrete implementation
[BinaryConverter(typeof(DataServiceConverter), ParameterName = "service")]
Task ProcessAsync(DataService service);

// ✅ Or use actor reference pattern
[BinaryConverter(typeof(StringConverter), ParameterName = "serviceActorId")]
Task ProcessAsync(string serviceActorId);
```

## Technical Details

### Analyzer Logic

The analyzer:
1. Registers for method declaration syntax nodes
2. Filters for methods in interfaces inheriting from `IQuarkActor`
3. Examines each parameter type (except `CancellationToken`)
4. Recursively checks generic type arguments and array element types
5. Reports errors for unsupported type patterns

### Type Detection

The analyzer identifies unsupported types using:

- **TypeKind.Delegate**: Detects all delegate types
- **Type name matching**: Detects `Action`, `Func`, `Expression<T>`, `IEnumerable<T>`, `IAsyncEnumerable<T>`
- **TypeKind.Interface**: Detects interface types (with exceptions for collection interfaces)
- **Recursive checking**: Validates type arguments in generic types

### Allowed Collection Interfaces

These collection interfaces are allowed as parameters:
- `IList<T>`
- `ICollection<T>`
- `IDictionary<K,V>`
- `ISet<T>`
- `IReadOnlyList<T>`
- `IReadOnlyCollection<T>`
- `IReadOnlyDictionary<K,V>`

**Why?** These interfaces are commonly used in .NET APIs and have well-defined serialization semantics. The binary converter system can handle them by serializing to concrete collection types.

## Best Practices

### 1. Materialize Data Early
Instead of passing `IEnumerable<T>`, materialize the data before the actor call:

```csharp
// ❌ Bad
var lazyQuery = dbContext.Orders.Where(x => x.Status == "Pending");
await orderActor.ProcessAsync(lazyQuery);  // ERROR: IEnumerable

// ✅ Good
var orders = await dbContext.Orders
    .Where(x => x.Status == "Pending")
    .ToListAsync();
await orderActor.ProcessAsync(orders);  // OK: List<T>
```

### 2. Use Value Objects Instead of Delegates
```csharp
// ❌ Bad
await actor.RegisterHandlerAsync(async () => await DoSomething());

// ✅ Good
public record HandlerCommand(string HandlerType, Dictionary<string, string> Parameters);
await actor.RegisterHandlerAsync(new HandlerCommand("DoSomething", parameters));
```

### 3. Use Query Objects Instead of Expressions
```csharp
// ❌ Bad
await actor.QueryAsync(x => x.Age > 18 && x.City == "Seattle");

// ✅ Good
public record PersonQuery(int? MinAge = null, string? City = null);
await actor.QueryAsync(new PersonQuery(MinAge: 18, City: "Seattle"));
```

### 4. Design for Serialization
When designing actor interfaces, think about how data will be transmitted:

```csharp
// ❌ Bad: Passes behavior
Task ProcessAsync(Func<int, bool> validator);

// ✅ Good: Passes data
[BinaryConverter(typeof(ValidationRulesConverter), ParameterName = "rules")]
Task ProcessAsync(ValidationRules rules);
```

## Related Analyzers

- **QUARK012**: Detects unsupported return types (IEnumerable, IAsyncEnumerable)
- **QUARK013**: Detects unsupported property types in actor interfaces
- **QUARK006**: Warns about potentially non-serializable parameters (less strict)

## Summary

The QUARK017 analyzer enforces the binary converter system's requirement for concrete, serializable types. By preventing the use of delegates, expression trees, and lazy evaluation types, it ensures that all actor method parameters can be reliably serialized for distributed actor calls.

**Key Takeaway**: Use concrete types (classes, structs, records, primitives, arrays) in actor interfaces, not lazy or behavior-based types.
