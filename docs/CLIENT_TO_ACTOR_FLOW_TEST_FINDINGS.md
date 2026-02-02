# Client-to-Silo-to-Mailbox-to-Actor Flow Test Findings

## Overview

This document summarizes the findings from the comprehensive integration tests created in `ClientSiloMailboxActorFlowTests.cs` to validate the complete message flow from client to actor without using Kestrel/HTTP layer.

## Test Environment

**Location:** `tests/Quark.Tests/ClientSiloMailboxActorFlowTests.cs`

**Test Count:** 9 comprehensive integration tests

**All Tests Status:** ✅ PASSING

## Complete Message Flow Validated

### 1. Request Path (Client → Silo → Mailbox → Actor)

```
CLIENT
  ↓
QuarkEnvelope created with:
  - MessageId (correlation)
  - ActorId (target actor instance)
  - ActorType (actor class name)
  - MethodName (method to invoke)
  - Payload (JSON-serialized parameters)
  ↓
TRANSPORT LAYER (GrpcQuarkTransport)
  - EnvelopeReceived event raised
  ↓
SILO (QuarkSilo.OnEnvelopeReceived)
  - Lookup dispatcher for ActorType
  - Get or create actor instance via ActorFactory
  - Get or create ActorInvocationMailbox
  - Post message to mailbox
  ↓
MAILBOX (ActorInvocationMailbox)
  - Sequential processing (one message at a time)
  - Bounded channel (capacity: 1000)
  ↓
DISPATCHER (IActorMethodDispatcher)
  - Deserialize payload as JSON
  - Invoke actor method
  - Serialize result as JSON
  ↓
ACTOR METHOD
  - Execute business logic
```

### 2. Response Path (Actor → Mailbox → Transport → Client)

```
ACTOR METHOD
  - Returns result
  ↓
DISPATCHER
  - Serialize result to JSON bytes
  ↓
MAILBOX
  - Create response envelope
  - Set ResponsePayload
  - Call transport.SendResponse()
  ↓
TRANSPORT LAYER
  - Send response envelope back to client
  - Correlate via MessageId
```

## Critical Findings

### 1. Protobuf Serialization is Required

**Finding:** All method parameters and return values must be Protobuf-serializable using generated message contracts.

**Evidence:**
- The generated dispatchers use `ProtoBuf.Serializer.Deserialize<T>()` for deserialization
- The generated proxies use `ProtoBuf.Serializer.Serialize()` for serialization
- Message contracts are auto-generated for each actor method (e.g., `ActorName_MethodNameRequest` and `ActorName_MethodNameResponse`)

**Implication:** If you're manually sending data to actors, it MUST be Protobuf-serialized using the generated message contracts. Raw byte arrays or JSON won't work.

**Fix Required:** Use generated message contracts:
```csharp
// ✅ CORRECT - Manual serialization (advanced use case)
var protoRequest = new ActorName_MethodNameRequest { Parameter = value };
byte[] payload;
using (var ms = new System.IO.MemoryStream())
{
    ProtoBuf.Serializer.Serialize(ms, protoRequest);
    payload = ms.ToArray();
}

// ✅ EVEN BETTER - Use generated proxy (recommended)
var proxy = client.GetActor<IActorName>(actorId);
var result = await proxy.MethodNameAsync(value);

// ❌ WRONG
var payload = System.Text.Encoding.UTF8.GetBytes(parameter);
```

### 2. Error Handling at Every Stage

**Finding:** The framework properly handles errors at each stage of the flow.

**Validated Error Scenarios:**
1. **Invalid Actor Type** - Returns error: "No dispatcher registered for actor type"
2. **Actor Method Exception** - Returns error envelope with exception message
3. **Mailbox Channel Closed** - Returns error: "Failed to post message to mailbox"
4. **Dispatcher Not Found** - Returns error before actor creation

**Evidence:** Tests `CompleteFlow_ActorException_ReturnsErrorEnvelope` and `ErrorPath_*` all validate proper error responses.

### 3. Sequential Processing Guarantee

**Finding:** Mailbox guarantees sequential processing of messages for each actor instance.

**Evidence:**
- Test `CompleteFlow_ConcurrentRequests_ProcessedSequentially` sends 10 concurrent increment operations
- Final count is exactly 10, proving no race conditions
- Messages are processed one at a time via bounded channel with single reader

**Implication:** Actor state is safe from concurrent modification within a single actor instance.

### 4. Message Correlation Works Correctly

**Finding:** MessageId correlation is maintained throughout the request-response cycle.

**Evidence:**
- Test `CompleteFlow_MessageIdCorrelation_MaintainsRequestResponseMapping` validates that each request gets a response with matching MessageId
- This enables async request-response patterns without confusion

### 5. Mailbox Handles High Volume

**Finding:** The mailbox can handle high-volume message posting without dropping messages.

**Evidence:**
- Test `CompleteFlow_MailboxBackpressure_HandlesHighVolume` sends 100 rapid operations
- All 100 messages are successfully processed
- Bounded channel provides backpressure when needed

### 6. Stateful Actors Maintain State

**Finding:** Actor state persists across multiple method invocations.

**Evidence:**
- Test `CompleteFlow_StatefulActor_MaintainsStateAcrossCalls` performs multiple operations on the same actor
- State (counter) is correctly maintained across separate calls
- Each actor instance has its own isolated state

## Potential Error Sources Identified

Based on the tests, here are the most likely error sources in the client-to-actor flow:

### 1. Serialization Errors (MOST LIKELY)

**Symptom:** Protobuf deserialization errors like "Attempted to read past the end of the stream" or "'X' is an invalid start of a value"

**Cause:** Sending non-Protobuf data or improperly formatted messages

**Fix:**
- Use the generated proxy client (handles all serialization automatically)
- If manually creating envelopes, use the generated Protobuf message contracts
- Ensure all types are Protobuf-serializable (primitives, strings, lists, etc.)
- Never send raw binary data or JSON - use Protobuf message contracts

### 2. Dispatcher Not Registered

**Symptom:** "No dispatcher registered for actor type 'XYZ'"

**Cause:** 
- Actor class missing `[Actor]` attribute
- Source generator not running
- Missing explicit reference to `Quark.Generators` project

**Fix:**
- Add `[Actor]` attribute to actor class
- Ensure generator project reference: `OutputItemType="Analyzer"`
- Clean and rebuild

### 3. Actor Creation Failure

**Symptom:** "Failed to create actor 'X' of type 'Y'"

**Cause:**
- Actor constructor throws exception
- ActorFactory not concrete implementation

**Fix:**
- Ensure actor has parameterless or (string actorId) constructor
- Check constructor doesn't throw exceptions

### 4. Method Not Found

**Symptom:** "Method 'MethodName' not found on actor type 'ActorType'"

**Cause:**
- Method name mismatch
- Method not public
- Method signature incompatible with dispatcher

**Fix:**
- Verify method name spelling
- Ensure method is public
- Ensure method returns Task, Task<T>, ValueTask, or ValueTask<T>

### 5. Mailbox Channel Closed

**Symptom:** "Failed to post message to mailbox (channel may be closed)"

**Cause:**
- Actor/silo shutting down
- Mailbox disposed
- Excessive errors causing mailbox shutdown

**Fix:**
- Check actor lifecycle management
- Ensure graceful shutdown procedures
- Investigate why mailbox was closed prematurely

## Test Usage

### Running the Tests

```bash
# Run all flow tests
dotnet test --filter "FullyQualifiedName~ClientSiloMailboxActorFlowTests"

# Run a specific test
dotnet test --filter "FullyQualifiedName~ClientSiloMailboxActorFlowTests.CompleteFlow_ClientToActorAndBack_SuccessfulInvocation"
```

### Extending the Tests

To add new test scenarios:

1. Add new test actor interfaces and implementations in the test class
2. Ensure actors have `[Actor]` attribute
3. Use Protobuf serialization with generated message contracts for all parameters
4. Set up mock transport to capture responses
5. Use `transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope)` to simulate message reception

## Recommendations

1. **Always Use Protobuf Serialization** - This is not optional. The framework uses Protobuf for all method parameters and return values. Use the generated proxy client for automatic handling, or use the generated message contracts for manual serialization.

2. **Prefer Generated Proxies** - The generated proxy client handles all serialization/deserialization automatically and provides type-safe access to actor methods.

3. **Test Error Paths** - Use these tests as a reference for validating error handling in your own actors.

4. **Monitor Mailbox Performance** - The bounded channel has a capacity of 1000. If you expect higher message volumes, consider the backpressure implications.

5. **Verify Message Correlation** - Always include MessageId in envelopes for proper request-response tracking.

6. **Test Concurrent Access** - Use the sequential processing tests as a baseline to verify actor state consistency.

## Conclusion

The comprehensive flow tests validate that the Quark actor invocation pipeline works correctly from client to actor and back, without needing the full HTTP/Kestrel stack. The tests verified that Protobuf serialization is the required format and validated that error handling, sequential processing, and message correlation all work as designed.

**Key Takeaway:** If you're experiencing errors in the client-to-actor flow, start by using the generated proxy client (recommended) or verifying that all data is properly Protobuf-serialized using the generated message contracts. This is the most common source of errors based on our test findings.
