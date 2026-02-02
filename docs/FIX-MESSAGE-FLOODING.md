# Fix: Message Flooding in ActorInvocationMailbox

## Problem Description

When an actor encountered an error during method invocation, the system experienced message flooding with the following symptoms:

1. **ProcessMessagesAsync floods with messages**: The mailbox processing loop was receiving duplicate messages
2. **PostAsync receives data when no client is sending**: Messages appeared to be received even though no client was actively sending
3. **Same QuarkEnvelope with different timestamps**: The same message (same MessageId, ActorId, MethodName) appeared multiple times with different Timestamp values

## Root Cause Analysis

The issue was identified in `QuarkTransportService.ActorStream`, specifically in the `OnEnvelopeReceived` event handler.

### The Echo Loop

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

### The Buggy Code

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

### Why This Caused Flooding

When an actor method throws an exception:

1. The error is caught in `ActorInvocationMailbox.ProcessMessagesAsync`
2. An error response envelope is created with the same MessageId but a NEW Timestamp
3. The error response is sent via `_transport.SendResponse(errorResponse)`
4. For local calls, this triggers the `EnvelopeReceived` event again
5. The buggy handler writes it back to the stream
6. The client receives it and might resend, creating a loop
7. Each iteration creates a new envelope with a different timestamp

## The Fix

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

### Key Changes

1. **Added filter check**: `if (envelope.ResponsePayload == null && !envelope.IsError)`
2. **Early return**: If it's an incoming request (no response payload and not an error), return immediately
3. **Only responses are written**: Only envelopes that are actual responses (have ResponsePayload or IsError=true) are written to the outgoing stream

## Verification

### Tests Passed

- ✅ All 9 ClientSiloMailboxActorFlowTests pass
- ✅ All 19 Quark.AwesomePizza.Tests pass
- ✅ Build succeeds with 0 errors
- ✅ No regressions detected

### Expected Behavior After Fix

1. **No echo loop**: Incoming request envelopes are not echoed back to the client
2. **Proper error handling**: Error responses are still sent correctly
3. **No duplicate messages**: Each message is processed exactly once
4. **Correct timestamps**: Each unique message has a single timestamp

## Related Code

- `src/Quark.Transport.Grpc/QuarkTransportService.cs` - The fixed file
- `src/Quark.Hosting/ActorInvocationMailbox.cs` - Mailbox processing
- `src/Quark.Hosting/QuarkSilo.cs` - Silo event handling
- `src/Quark.Transport.Grpc/GrpcQuarkTransport.cs` - Transport layer

## Testing Recommendations

When testing error handling in actors:

1. **Verify no message duplication**: Check that each MessageId appears only once
2. **Check timestamp consistency**: Verify timestamps are consistent for the same message
3. **Monitor mailbox depth**: Ensure the mailbox doesn't accumulate messages
4. **Test error responses**: Verify error responses are delivered correctly without triggering loops

## Future Improvements

Consider adding:

1. **Message deduplication**: Add MessageId tracking to prevent processing duplicates
2. **Dead letter queue**: Move persistently failing messages to a dead letter queue
3. **Circuit breaker**: Implement circuit breaker pattern for repeatedly failing actors
4. **Metrics**: Add metrics for message processing rates and error rates
