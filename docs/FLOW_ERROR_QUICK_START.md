# Quick Start: Debugging Client-to-Silo-to-Actor Flow Errors

## Problem

You're experiencing errors in the data flow from client → silo → mailbox → actor → response, and you need to identify the root cause without running the full Kestrel HTTP stack.

## Solution

Run the comprehensive integration tests that isolate and validate each stage of the actor invocation pipeline:

```bash
cd /home/runner/work/Quark/Quark
dotnet test tests/Quark.Tests/Quark.Tests.csproj --filter "FullyQualifiedName~ClientSiloMailboxActorFlowTests"
```

**Result:** ✅ All 9 tests pass, validating the complete flow works correctly.

## Most Common Error & Fix

### Error Symptom
```
Attempted to read past the end of the stream.
```
or
```
'X' is an invalid start of a value. Path: $ | LineNumber: 0 | BytePositionInLine: 0.
```

### Root Cause
You're sending data that isn't properly Protobuf-serialized.

### ❌ Wrong Way
```csharp
var payload = System.Text.Encoding.UTF8.GetBytes("Hello World");
var envelope = new QuarkEnvelope(messageId, actorId, actorType, methodName, payload);
```

### ✅ Correct Way (Manual Serialization)
```csharp
// Create the protobuf request message
var protoRequest = new YourActor_MethodNameRequest { Parameter = "Hello World" };
byte[] payload;
using (var ms = new System.IO.MemoryStream())
{
    ProtoBuf.Serializer.Serialize(ms, protoRequest);
    payload = ms.ToArray();
}
var envelope = new QuarkEnvelope(messageId, actorId, actorType, methodName, payload);
```

### ✅ Even Better Way (Use Generated Proxies)
```csharp
// Use the generated proxy - it handles all serialization automatically
var client = new ClusterClient(transport);
var proxy = client.GetActor<IYourActor>(actorId);
var result = await proxy.MethodNameAsync("Hello World");
```

### Why?
The generated actor method dispatchers use **Protobuf serialization** (`ProtoBuf.Serializer.Deserialize<T>(payload)`) to extract parameters. They expect valid Protobuf-serialized data, not raw text or JSON.

The proxy generator automatically creates Protobuf message contracts (`YourActor_MethodNameRequest` and `YourActor_MethodNameResponse`) for each actor method, ensuring type-safe, efficient serialization.

## Other Common Errors

### 1. "No dispatcher registered for actor type 'XYZ'"

**Cause:** Missing `[Actor]` attribute or source generator not running

**Fix:**
```csharp
[Actor(Name = "MyActor")]  // ✅ Add this attribute
public class MyActor : ActorBase
{
    // ...
}
```

Then rebuild: `dotnet clean && dotnet build`

### 2. "Failed to create actor 'X' of type 'Y'"

**Cause:** Actor constructor throws exception or wrong constructor signature

**Fix:**
```csharp
[Actor]
public class MyActor : ActorBase
{
    // ✅ Correct - takes actorId parameter
    public MyActor(string actorId) : base(actorId) { }
    
    // ❌ Wrong - throws exception
    // public MyActor(string actorId) : base(actorId) 
    // {
    //     throw new Exception("boom");
    // }
}
```

### 3. "Method 'MethodName' not found on actor type"

**Cause:** Method name mismatch or method not public/async

**Fix:**
```csharp
// ✅ Correct - public, async, returns Task
public async Task<string> ProcessDataAsync(string input)
{
    return await Task.FromResult($"Processed: {input}");
}

// ❌ Wrong - not async
// public string ProcessData(string input) { ... }

// ❌ Wrong - not public
// private async Task<string> ProcessDataAsync(string input) { ... }
```

## How the Tests Help

The tests in `ClientSiloMailboxActorFlowTests.cs` validate:

1. **✅ Successful Invocation** - Basic happy path works
2. **✅ Stateful Actors** - State persists across calls
3. **✅ Error Handling** - Exceptions are properly caught and returned
4. **✅ Invalid Actor Types** - Missing dispatchers are detected
5. **✅ Sequential Processing** - No race conditions in mailbox
6. **✅ High Volume** - Can handle 100+ concurrent messages
7. **✅ Message Correlation** - Request-response mapping works
8. **✅ Dispatcher Errors** - Missing dispatchers caught early
9. **✅ Exception Propagation** - Actor exceptions become error envelopes

## Testing Your Own Actors

Use the test pattern to validate your actors:

```csharp
[Fact]
public async Task MyActor_MyMethod_Works()
{
    // 1. Set up the stack (client, silo, transport)
    var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo");
    
    // 2. Capture response
    QuarkEnvelope? response = null;
    transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
        .Callback<QuarkEnvelope>(r => response = r);
    
    // 3. Create envelope with Protobuf-serialized parameters
    var protoRequest = new MyActor_MyMethodRequest { Parameter = myParameter };
    byte[] payload;
    using (var ms = new System.IO.MemoryStream())
    {
        ProtoBuf.Serializer.Serialize(ms, protoRequest);
        payload = ms.ToArray();
    }
    
    var envelope = new QuarkEnvelope(
        messageId: Guid.NewGuid().ToString(),
        actorId: "my-actor-1",
        actorType: "MyActor",
        methodName: "MyMethod",
        payload: payload);  // ✅ Protobuf!
    
    // 4. Simulate transport receiving the envelope
    await Task.Delay(50);  // Let silo start
    transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope);
    await Task.Delay(500);  // Let mailbox process
    
    // 5. Assert response
    Assert.NotNull(response);
    Assert.False(response.IsError);
    
    // 6. Deserialize result
    using (var ms = new System.IO.MemoryStream(response.ResponsePayload))
    {
        var result = ProtoBuf.Serializer.Deserialize<MyActor_MyMethodResponse>(ms);
        Assert.Equal(expectedValue, result.Result);
    }
}
```

## For More Details

See `docs/CLIENT_TO_ACTOR_FLOW_TEST_FINDINGS.md` for:
- Complete flow diagram
- All error scenarios
- Detailed recommendations
- Test extension guide

## Quick Checklist

When debugging actor invocation errors:

- [ ] Are parameters Protobuf-serialized? (Use generated message contracts and `ProtoBuf.Serializer.Serialize()`)
- [ ] Or are you using the generated proxy? (Recommended - handles serialization automatically)
- [ ] Does actor have `[Actor]` attribute?
- [ ] Is project referencing `Quark.Generators` with `OutputItemType="Analyzer"`?
- [ ] Are actor methods public and return Task/Task<T>?
- [ ] Does actor have correct constructor: `public MyActor(string actorId)`?
- [ ] Are response payloads deserialized as Protobuf? (Use generated response message contracts)

If all checklist items pass and you still have errors, run the flow tests to isolate the issue:

```bash
dotnet test --filter "FullyQualifiedName~ClientSiloMailboxActorFlowTests"
```
