# Stateful Actor Audit for AwesomePizza

## Executive Summary

This document audits the current state of Actor implementation in the AwesomePizza project and provides recommendations for proper state management using `StatefulActorBase` and the `[QuarkState]` attribute.

**Date**: February 2, 2026  
**Status**: ✅ ActorContext integrated, ⚠️ State persistence not yet implemented

---

## Current State Analysis

###  Actor Base Classes Available

1. **ActorBase** ✅
   - Basic actor lifecycle (OnActivateAsync, OnDeactivateAsync)
   - Supervision support (ISupervisor)
   - Child actor management (SpawnChildAsync)
   - Dependency injection support (ServiceScope)
   - **NEW**: ActorContext integration (Context property, OnActivateWithContextAsync)

2. **StatefulActorBase** ✅
   - Inherits from ActorBase
   - State persistence support via `GetStorage<TState>(providerName)`
   - Requires `IStateStorageProvider` from DI
   - Supports optimistic concurrency via versioning

### ActorContext Integration Status ✅

**Completed Changes:**
- `ActorBase.OnActivateAsync` now creates ActorContext automatically
- `ActorBase.OnDeactivateAsync` now creates ActorContext automatically
- New `ActorBase.Context` property provides access to current IActorContext
- New protected methods `OnActivateWithContextAsync` and `OnDeactivateWithContextAsync` for derived classes
- AsyncLocal propagation ensures context flows across async boundaries
- 11 passing tests verify ActorContext integration

**Benefits:**
- Automatic correlation ID generation for distributed tracing
- Request ID tracking for debugging
- Metadata dictionary for contextual information
- No manual context management required

---

## AwesomePizza Actors Audit

### 1. OrderActor ⚠️ **Needs State Persistence**

**Current Implementation:**
- Extends `ActorBase` 
- Uses in-memory field `_state` for OrderState
- Manual state management (no persistence)
- State lost on actor deactivation

**Issues:**
1. ❌ State is not persisted - lost on deactivation/restart
2. ❌ No optimistic concurrency control despite having ETag field
3. ❌ Comments say "TODO: await SaveStateAsync()" but never implemented
4. ❌ No state loading in OnActivateAsync
5. ⚠️ Multiple state mutations without persistence calls

**Recommended Changes:**
```csharp
// Change from:
public class OrderActor : ActorBase, IOrderActor
{
    private OrderState? _state;
    
    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // In a full implementation, this would load state from Redis
        return Task.CompletedTask;
    }
}

// To:
public partial class OrderActor : StatefulActorBase, IOrderActor
{
    [QuarkState("redis")]
    public OrderState? State { get; private set; }
    
    protected override async Task OnActivateWithContextAsync(CancellationToken cancellationToken = default)
    {
        // Generated method LoadStateAsync() will be available
        await LoadStateAsync(cancellationToken);
    }
    
    public async Task<CreateOrderResponse> CreateOrderAsync(...)
    {
        // ... create state ...
        State = new OrderState(...);
        
        // Generated method SaveStateAsync() will be available
        await SaveStateAsync(cancellationToken);
        
        return response;
    }
}
```

**Source Generator Notes:**
- `[QuarkState("redis")]` attribute triggers code generation
- Generated methods: `LoadStateAsync()`, `SaveStateAsync()`, `DeleteStateAsync()`
- Supports optimistic concurrency with version tracking
- AOT-safe JSON serialization via generated JsonSerializerContext

**Known Issue:**
- Source generator currently has issues with types from other assemblies
- OrderState is in `Quark.AwesomePizza.Shared.Models` namespace
- Generated code may not include proper using statements
- **Workaround**: Define state types in the same assembly as the actor, or fix generator

### 2. DriverActor ⚠️ **Needs State Persistence**

**Current Implementation:**
- Extends `ActorBase`
- Uses in-memory fields `_state`, `_deliveryCount`
- No state persistence

**Recommended Changes:**
```csharp
public partial class DriverActor : StatefulActorBase, IDriverActor
{
    [QuarkState("redis")]
    public DriverState? State { get; private set; }
    
    protected override async Task OnActivateWithContextAsync(CancellationToken cancellationToken = default)
    {
        await LoadStateAsync(cancellationToken);
    }
}
```

### 3. ChefActor ✅ **Acceptable (Stateless Worker)**

**Current Implementation:**
- Extends `ActorBase`
- Minimal state (availability flag, current order ID)
- Short-lived operations

**Recommendation:**
- ✅ Keep as ActorBase (acceptable for stateless workers)
- State is transient and can be reconstructed
- Consider adding `[StatelessWorker]` attribute if appropriate

### 4. KitchenActor ⚠️ **Needs State Persistence**

**Current Implementation:**
- Extends `ActorBase`
- Manages order queue and active orders
- No state persistence

**Recommended Changes:**
```csharp
public partial class KitchenActor : StatefulActorBase, IKitchenActor
{
    [QuarkState("redis")]
    public KitchenState? State { get; private set; }
}
```

### 5. InventoryActor ⚠️ **Critical - Needs State Persistence**

**Current Implementation:**
- Extends `ActorBase`
- Manages inventory levels
- **CRITICAL**: Inventory data MUST be persisted

**Priority**: HIGH
**Reason**: Inventory is business-critical data that cannot be lost

### 6. RestaurantActor ⚠️ **Needs State Persistence**

**Current Implementation:**
- Extends `ActorBase`
- Aggregates metrics
- No state persistence

**Recommended Changes:**
```csharp
public partial class RestaurantActor : StatefulActorBase, IRestaurantActor
{
    [QuarkState("redis")]
    public RestaurantState? State { get; private set; }
}
```

---

## State Storage Configuration

### Current Setup

The Silo project (`Quark.AwesomePizza.Silo.csproj`) already references:
- ✅ `Quark.Hosting`
- ✅ `Quark.Storage.Redis`
- ✅ `Quark.Clustering.Redis`
- ✅ `Quark.Generators` (for source generation)

### Required DI Configuration

The `Program.cs` needs to register state storage:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register Redis state storage provider
builder.Services.AddSingleton<IStateStorageProvider>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") 
                          ?? "localhost:6379";
    return new StateStorageProvider(connectionString);
});

var app = builder.Build();
app.Run();
```

### Storage Provider Configuration

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "StateStorage": {
    "DefaultProvider": "redis",
    "Providers": {
      "redis": {
        "Type": "Quark.Storage.Redis.RedisStateStorage",
        "ConnectionString": "localhost:6379",
        "Database": 0
      }
    }
  }
}
```

---

## Implementation Checklist

### Phase 1: Fix Source Generator ⚠️
- [ ] Fix StateSourceGenerator to handle types from other assemblies
- [ ] Ensure proper using statements in generated code
- [ ] Test with OrderState from Quark.AwesomePizza.Shared.Models

### Phase 2: Convert OrderActor (Critical)
- [ ] Change base class to StatefulActorBase
- [ ] Add [QuarkState("redis")] to State property
- [ ] Make class partial
- [ ] Update OnActivateAsync to load state
- [ ] Add SaveStateAsync calls after all state mutations (9 methods)
- [ ] Remove manual _state field
- [ ] Handle ConcurrencyException for optimistic concurrency

### Phase 3: Convert Other Actors
- [ ] DriverActor → StatefulActorBase
- [ ] KitchenActor → StatefulActorBase
- [ ] InventoryActor → StatefulActorBase (HIGH PRIORITY)
- [ ] RestaurantActor → StatefulActorBase

### Phase 4: Configuration
- [ ] Add IStateStorageProvider to DI container
- [ ] Configure Redis connection string
- [ ] Add state storage configuration to appsettings.json
- [ ] Test state persistence with Redis

### Phase 5: Testing
- [ ] Update existing tests to use StatefulActorBase
- [ ] Add tests for state persistence
- [ ] Add tests for optimistic concurrency
- [ ] Test state loading on activation
- [ ] Test state saving on mutations

---

## ActorContext Usage Examples

With the new ActorContext integration, actors can now access contextual information:

```csharp
public class OrderActor : ActorBase, IOrderActor
{
    public async Task<CreateOrderResponse> CreateOrderAsync(
        CreateOrderRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Context is automatically available via base class
        var correlationId = Context?.CorrelationId;
        var requestId = Context?.RequestId;
        
        // Add custom metadata for tracing
        Context?.SetMetadata("orderId", ActorId);
        Context?.SetMetadata("customerId", request.CustomerId);
        
        // Create order...
        _state = new OrderState(...);
        
        // Metadata flows through async calls
        await NotifyKitchenAsync();
        
        return response;
    }
    
    private async Task NotifyKitchenAsync()
    {
        // Context is still available here via AsyncLocal
        var correlationId = Context?.CorrelationId; // Same as parent method
        
        // Can access metadata set earlier
        var customerId = Context?.GetMetadata<string>("customerId");
    }
}
```

---

## Benefits of Migration

### With ActorContext (Already Implemented) ✅
1. **Distributed Tracing**: Correlation IDs automatically flow through actor calls
2. **Request Tracking**: Request IDs help debug specific operations
3. **Contextual Logging**: Log metadata enriches logs with business context
4. **No Manual Management**: Context automatically managed by framework

### With State Persistence (To Be Implemented)
1. **Durability**: State survives actor deactivation and restart
2. **Fault Tolerance**: Actors can be recovered after failures
3. **Scalability**: Actors can move between silos without data loss
4. **Optimistic Concurrency**: ETag-based versioning prevents race conditions
5. **Performance**: Generated code is AOT-compiled (zero reflection)

---

## Recommendations

### Immediate Actions
1. ✅ ActorContext is now integrated - actors have context automatically
2. ⚠️ Fix StateSourceGenerator namespace handling
3. ⚠️ Convert OrderActor to use StatefulActorBase
4. ⚠️ Add state storage DI registration

### Short-term Actions
1. Convert all AwesomePizza actors to StatefulActorBase
2. Add comprehensive state persistence tests
3. Document state management patterns

### Long-term Actions
1. Add distributed tracing integration (OpenTelemetry)
2. Implement state versioning/migration strategy
3. Add state backup/restore capabilities
4. Consider adding state caching layer

---

## References

- [Quark StatefulActorBase](../src/Quark.Core.Actors/StatefulActorBase.cs)
- [State Source Generator](../src/Quark.Generators/StateSourceGenerator.cs)
- [IStateStorage Interface](../src/Quark.Abstractions/Persistence/IStateStorage.cs)
- [ActorContext Implementation](../src/Quark.Core.Actors/ActorContext.cs)
- [ActorContext Tests](../tests/Quark.Tests/ActorContextTests.cs)

---

**Document Version**: 1.0  
**Last Updated**: February 2, 2026  
**Next Review**: When StateSourceGenerator is fixed
