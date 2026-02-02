# Architectural Solution: Separating Incoming and Outgoing Flows

## Problem Statement

The system was experiencing a self-loopback issue where responses were being processed as new requests, creating infinite loops. The root cause was using the same `EnvelopeReceived` event for both incoming requests and outgoing responses.

## Solution: Dual Filtering Pattern

Instead of creating separate events (which would require major refactoring), we implemented a **dual filtering pattern** where both subscribers filter the same event differently.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     EnvelopeReceived Event                       │
│                     (Single Event Source)                        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Raised by:
                              │ - QuarkTransportService (incoming from gRPC)
                              │ - SendResponse (outgoing responses)
                              │
        ┌─────────────────────┴─────────────────────┐
        │                                           │
        ▼                                           ▼
┌──────────────────────┐                  ┌──────────────────────┐
│   QuarkSilo          │                  │ QuarkTransportService│
│   Subscriber #1      │                  │   Subscriber #2      │
├──────────────────────┤                  ├──────────────────────┤
│ Filter Logic:        │                  │ Filter Logic:        │
│                      │                  │                      │
│ if (ResponsePayload  │                  │ if (ResponsePayload  │
│     != null ||       │                  │     == null &&       │
│     IsError)         │                  │     !IsError)        │
│   return;  // Skip   │                  │   return;  // Skip   │
│                      │                  │                      │
│ ✓ PROCESSES REQUESTS │                  │ ✓ WRITES RESPONSES   │
│ ✗ SKIPS RESPONSES    │                  │ ✗ SKIPS REQUESTS     │
└──────────────────────┘                  └──────────────────────┘
         │                                           │
         ▼                                           ▼
┌──────────────────────┐                  ┌──────────────────────┐
│ Post to Actor        │                  │ Write to gRPC        │
│ Mailbox              │                  │ Stream               │
└──────────────────────┘                  └──────────────────────┘
```

## Flow Examples

### Example 1: Incoming Request

```
1. Client sends request via gRPC
2. QuarkTransportService receives and raises EnvelopeReceived(request)
   - ResponsePayload = null
   - IsError = false
3. Event reaches both subscribers:
   
   QuarkSilo Filter:
   - Check: ResponsePayload != null? NO
   - Check: IsError? NO
   - Result: NOT a response → CONTINUE processing
   - Action: Post to mailbox ✓
   
   QuarkTransportService Filter:
   - Check: ResponsePayload == null? YES
   - Check: !IsError? YES
   - Result: NOT a response → SKIP
   - Action: Return early ✓
   
4. Actor processes request in mailbox
5. Result: Request processed once, no loop
```

### Example 2: Outgoing Response

```
1. Actor finishes processing
2. SendResponse raises EnvelopeReceived(response)
   - ResponsePayload = <data> OR IsError = true
3. Event reaches both subscribers:
   
   QuarkSilo Filter:
   - Check: ResponsePayload != null? YES OR IsError? YES
   - Result: IS a response → SKIP
   - Action: Return early ✓
   
   QuarkTransportService Filter:
   - Check: ResponsePayload == null? NO OR !IsError? NO
   - Result: IS a response → CONTINUE
   - Action: Write to gRPC stream ✓
   
4. Client receives response
5. Result: Response sent once, no reprocessing, no loop
```

## Key Benefits

1. **No Architectural Changes**: Uses existing event infrastructure
2. **Clear Separation**: Each subscriber handles only its flow
3. **No Loopback**: Responses never get reprocessed as requests
4. **Maintainable**: Filter logic is simple and explicit
5. **Testable**: Easy to verify each flow independently

## Filter Logic Summary

| Envelope Type | ResponsePayload | IsError | QuarkSilo Action | TransportService Action |
|---------------|-----------------|---------|------------------|-------------------------|
| Request       | null            | false   | ✓ Process        | ✗ Skip                 |
| Success Response | <data>       | false   | ✗ Skip          | ✓ Send                 |
| Error Response | null           | true    | ✗ Skip          | ✓ Send                 |
| Error with Data | <data>        | true    | ✗ Skip          | ✓ Send                 |

## Implementation Details

### QuarkSilo.OnEnvelopeReceived

```csharp
private void OnEnvelopeReceived(object? sender, QuarkEnvelope envelope)
{
    // Filter: Only process requests, skip responses
    if (envelope.ResponsePayload != null || envelope.IsError)
    {
        _logger.LogTrace("Skipping response envelope {MessageId}", envelope.MessageId);
        return;
    }
    
    // Process incoming request...
    PostToActorMailbox(envelope);
}
```

### QuarkTransportService.OnEnvelopeReceived

```csharp
void OnEnvelopeReceived(object? sender, QuarkEnvelope envelope)
{
    // Filter: Only write responses, skip requests
    if (envelope.ResponsePayload == null && !envelope.IsError)
    {
        return; // This is a request, not a response
    }
    
    // Write outgoing response to gRPC stream
    WriteToGrpcStream(envelope);
}
```

## Comparison with Alternative Approaches

### Alternative 1: Separate Events (Rejected)
- Create `RequestReceived` and `ResponseReady` events
- **Cons:** Major refactoring, breaks existing code, more complex

### Alternative 2: Direct Method Calls (Rejected)
- Call `QuarkTransportService.SendResponse()` directly
- **Cons:** Tight coupling, harder to test, less flexible

### Alternative 3: Dual Filtering (Chosen) ✓
- Use same event with different filters
- **Pros:** Minimal changes, clear separation, maintainable

## Testing Strategy

1. **Unit Tests**: Verify each filter independently
2. **Integration Tests**: Test complete request-response cycles
3. **Error Tests**: Verify error responses don't cause loops
4. **Load Tests**: Ensure no performance regression

## Conclusion

The dual filtering pattern achieves clean separation of incoming and outgoing flows without requiring architectural changes. By having each subscriber filter the same event differently, we prevent self-loopback while maintaining the existing event-driven architecture.
