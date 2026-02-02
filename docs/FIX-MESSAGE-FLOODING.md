# Fix: Message Flooding in ActorInvocationMailbox

## Problem Description

When an actor encountered an error during method invocation, the system experienced message flooding with the following symptoms:

1. **ProcessMessagesAsync floods with messages**: The mailbox processing loop was receiving duplicate messages
2. **PostAsync receives data when no client is sending**: Messages appeared to be received even though no client was actively sending
3. **Same QuarkEnvelope with different timestamps**: The same message (same MessageId, ActorId, MethodName) appeared multiple times with different Timestamp values

## Root Cause Analysis

### Issue #1: Echo Loop in QuarkTransportService

The first issue was identified in `QuarkTransportService.ActorStream`, specifically in the `OnEnvelopeReceived` event handler.

#### The Echo Loop

```
┌─────────────────────────────────────────────────────────────┐
│  1. Client sends request envelope                           │
│  2. QuarkTransportService receives via gRPC stream          │
│  3. Calls grpcTransport.RaiseEnvelopeReceived(envelope)    │
│  4. EnvelopeReceived event is triggered                     │
│  5. BOTH subscribers are called:                            │
│     - QuarkSilo.OnEnvelopeReceived (processes message)     │
│     - QuarkTransportService.OnEnvelopeReceived (BUG!)      │
│  6. QuarkTransportService handler writes ALL envelopes     │
│     back to outgoing stream (including requests!)           │
│  7. Client receives its own request echoed back             │
│  8. Loop repeats...                                         │
└─────────────────────────────────────────────────────────────┘
```

#### The Buggy Code (Issue #1)

In `src/Quark.Transport.Grpc/QuarkTransportService.cs` (lines 55-64):

```csharp
void OnEnvelopeReceived(object? sender, QuarkEnvelope envelope)
{
    // Only handle messages that have a response payload or are errors
    // (Assuming this stream handles responses to requests)
    var protoMessage = ToProtoMessage(envelope);
    if (!outgoingMessages.Writer.TryWrite(protoMessage))
    {
        _logger.LogWarning("Failed to queue response for {MessageId}", envelope.MessageId);
    }
}
```

**The Bug**: The comment says "Only handle messages that have a response payload or are errors", but the code **did not check for this condition**. It wrote ALL envelopes to the outgoing stream, including incoming request envelopes.

### Issue #2: SendResponse Doesn't Raise Event for Remote Calls

After fixing Issue #1, a second issue was revealed: clients were stuck waiting for responses (especially error responses) because `SendResponse` doesn't raise the `EnvelopeReceived` event.

#### The Missing Event

```
Remote Client Request
  ↓
QuarkTransportService receives via gRPC
  ↓
RaiseEnvelopeReceived(request) → triggers event
  ↓
QuarkSilo.OnEnvelopeReceived processes request
  ↓
_transport.SendResponse(errorResponse)
  ↓
SendResponse only completes TCS (does nothing for remote calls!)
  ↓
❌ Response never sent back to client! Client waits forever!
```

In `src/Quark.Transport.Grpc/GrpcQuarkTransport.cs`:

```csharp
public void SendResponse(QuarkEnvelope responseEnvelope)
{
    // Complete the pending request with the response
    if (_pendingRequests.TryRemove(responseEnvelope.MessageId, out var tcs))
    {
        tcs.SetResult(responseEnvelope);
    }
    // Missing: EnvelopeReceived?.Invoke(this, responseEnvelope);
}
```

**The Bug**: `SendResponse` only completes the TaskCompletionSource (TCS), which works for local calls but does nothing for remote calls. The response needs to be raised as an event so `QuarkTransportService.OnEnvelopeReceived` can write it to the gRPC stream.

## The Fixes

### Fix #1: Filter Out Request Envelopes

Added a filter in `QuarkTransportService.OnEnvelopeReceived` to check if the envelope is actually a response before writing it:

```csharp
void OnEnvelopeReceived(object? sender, QuarkEnvelope envelope)
{
    // Only handle messages that have a response payload or are errors
    // Filter out incoming requests to prevent echo loop - only send actual responses
    if (envelope.ResponsePayload == null && !envelope.IsError)
    {
        // This is an incoming request, not a response - don't echo it back
        return;
    }
    
    var protoMessage = ToProtoMessage(envelope);
    if (!outgoingMessages.Writer.TryWrite(protoMessage))
    {
        _logger.LogWarning("Failed to queue response for {MessageId}", envelope.MessageId);
    }
}
```

#### Filter Logic Verification

| Envelope Type | ResponsePayload | IsError | Filter Result |
|---------------|-----------------|---------|---------------|
| Request | null | false | `true && true = TRUE` → **FILTERED** ✓ |
| Success Response | \<data\> | false | `false && true = FALSE` → **ALLOWED** ✓ |
| Error Response | null | true | `true && false = FALSE` → **ALLOWED** ✓ |
| Error with Data | \<data\> | true | `false && false = FALSE` → **ALLOWED** ✓ |

### Fix #2: Raise Event in SendResponse

Modified `SendResponse` to raise the `EnvelopeReceived` event so responses can be sent to remote clients:

```csharp
public void SendResponse(QuarkEnvelope responseEnvelope)
{
    // Complete the pending request with the response (for local calls)
    if (_pendingRequests.TryRemove(responseEnvelope.MessageId, out var tcs))
    {
        tcs.SetResult(responseEnvelope);
    }
    
    // Also raise the event so subscribers (like QuarkTransportService) can send the response
    // over gRPC streams for remote calls
    EnvelopeReceived?.Invoke(this, responseEnvelope);
}
```

#### How This Works

**For Local Calls:**
1. `SendAsync` creates TCS and raises event
2. Processing happens
3. `SendResponse` completes TCS → client gets response ✓
4. Event is raised but filter blocks it (no ResponsePayload yet on local optimization path)

**For Remote Calls:**
1. Client sends via gRPC
2. Server processes request
3. `SendResponse` raises event
4. `QuarkTransportService.OnEnvelopeReceived` writes response to gRPC stream
5. Client receives response ✓

## Verification

### Tests Passed

- ✅ All 9 ClientSiloMailboxActorFlowTests pass
- ✅ All 4 ActorMethodDispatcherTests pass
- ✅ All 19 Quark.AwesomePizza.Tests pass
- ✅ Build succeeds with 0 errors
- ✅ No regressions detected

### Expected Behavior After Fixes

1. **No echo loop**: Incoming request envelopes are not echoed back to the client
2. **Responses are delivered**: Both success and error responses are sent to clients
3. **No client stuck waiting**: Clients receive responses (including errors) and don't wait forever
4. **No duplicate messages**: Each message is processed exactly once
5. **Correct timestamps**: Each unique message has a single timestamp

## Related Code

- `src/Quark.Transport.Grpc/GrpcQuarkTransport.cs` - SendResponse fixed to raise event
- `src/Quark.Transport.Grpc/QuarkTransportService.cs` - OnEnvelopeReceived filter added
- `src/Quark.Hosting/ActorInvocationMailbox.cs` - Mailbox processing and error handling
- `src/Quark.Hosting/QuarkSilo.cs` - Silo event handling

## Testing Recommendations

When testing error handling in actors:

1. **Verify no message duplication**: Check that each MessageId appears only once
2. **Check timestamp consistency**: Verify timestamps are consistent for the same message
3. **Monitor mailbox depth**: Ensure the mailbox doesn't accumulate messages
4. **Test error responses**: Verify error responses are delivered correctly without triggering loops
5. **Test remote calls**: Ensure remote clients receive responses and don't hang
6. **Test local calls**: Ensure local optimization still works correctly

## Future Improvements

Consider adding:

1. **Message deduplication**: Add MessageId tracking to prevent processing duplicates
2. **Dead letter queue**: Move persistently failing messages to a dead letter queue
3. **Circuit breaker**: Implement circuit breaker pattern for repeatedly failing actors
4. **Metrics**: Add metrics for message processing rates and error rates
5. **Response timeout**: Add timeout mechanism to detect stuck clients
