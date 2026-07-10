# Concurrent stream fan-out + serialize-once for gateway subscribers

**Issue:** #145 — *Stream fan-out delivers to subscribers sequentially and reserializes per TCP subscriber*
**Status:** Design approved (2026-07-10)
**Area:** streaming — `Quark.Streaming.InMemory`, `Quark.Streaming.Abstractions`, `Quark.Runtime`
**Severity:** medium (performance hardening; no behavior change observable to correct callers)

## Problem

Two compounding issues in the in-memory stream provider's publish path:

1. **Sequential delivery.** `StreamSubscriptionRegistry.PublishAsync` awaits each subscriber's
   `OnNextAsync` one at a time in a plain `foreach`, for both the typed (`_subs`) and the
   untyped/gateway (`_untyped`) subscriber lists. The list is snapshotted under a short `lock`, so
   this is not a held-lock block, but a slow or backpressured subscriber (e.g. a TCP client) still
   stalls delivery to every subscriber queued behind it. Delivery latency for the whole fan-out is
   the *sum* of per-subscriber latencies instead of the *max*.

2. **Per-subscriber reserialization.** `GatewayClientSubscription.OnNextAsync` independently resolves
   a codec and serializes the published item into a fresh `ArrayBufferWriter<byte>` on every call.
   One `GatewayClientSubscription` instance exists per TCP-connected subscriber, so N gateway
   subscribers to the same stream serialize the same item N times. All gateway subscriptions to a
   stream share the same `ICodecProvider`, so every one produces byte-identical wire output — the
   repeated work is pure waste.

`PublishErrorAsync` and `PublishCompletedAsync` share the same sequential `foreach` shape (typed
`_subs` only).

## Goals

- Fan out concurrently so the slowest subscriber, not the sum, determines publish latency.
- Serialize a published item **once** per publish for the gateway/untyped path and share the encoded
  bytes across every gateway subscriber on that stream.
- Preserve all currently observable semantics: backpressure, `AggregateException` error propagation,
  and per-subscriber FIFO ordering.
- Keep `Quark.Streaming.InMemory` codec-agnostic and keep all serialization (and its AOT annotations)
  in `Quark.Runtime`.

## Non-goals

- No change to `PublishErrorAsync`/`PublishCompletedAsync` **routing**: they fan out to typed `_subs`
  only today (no untyped propagation). That pre-existing behavior is unchanged; only the fan-out loop
  becomes concurrent.
- No fire-and-forget / decoupled-delivery model. Publishers still await all deliveries.
- No bounded-concurrency throttle. Full `Task.WhenAll`; revisit only if very large fan-outs prove a
  thundering-herd problem.
- No object pooling of `SharedStreamItem`. One allocation per untyped publish; optimize later if it
  shows up in profiling (YAGNI).

## Decisions

| Question | Decision |
|---|---|
| Fan-out contract | **Await all, concurrently** (`Task.WhenAll`). Preserves backpressure + `AggregateException`; only wall-clock latency improves. |
| Serialize-once architecture | **Additive interface** — a `SharedStreamItem` holder + `ISharedEncodingStreamObserver` implemented by `GatewayClientSubscription`. No breaking change; registry stays codec-agnostic. |
| Path scope | **All four loops** — `PublishAsync` (typed + untyped), `PublishErrorAsync`, `PublishCompletedAsync` — get concurrent fan-out, for a consistent pattern. Serialize-once applies to the untyped path only. |

## Design

### 1. New shared-encoding primitives — `Quark.Streaming.Abstractions`

A per-publish holder that carries the raw item and memoizes its encoded bytes:

```csharp
/// <summary>
///     Carries a single published stream item through fan-out so that observers which serialize the
///     item (e.g. gateway/TCP subscribers) encode it at most once and share the resulting bytes.
/// </summary>
public sealed class SharedStreamItem(object item)
{
    private ReadOnlyMemory<byte>? _encoded;

    /// <summary>The raw published item, for observers that consume it without serialization.</summary>
    public object Item => item;

    /// <summary>
    ///     Returns the encoded bytes, invoking <paramref name="encode"/> only on the first call and
    ///     memoizing the result for subsequent callers.
    /// </summary>
    /// <remarks>
    ///     Fan-out invokes each observer's callback synchronously up to its first <c>await</c>
    ///     (see <see cref="StreamSubscriptionRegistry"/>), and encoding happens in that synchronous
    ///     prefix, so this runs single-threaded — no lock is required. Even if a future refactor broke
    ///     that invariant, the worst case is a redundant, deterministic re-encode producing identical
    ///     bytes; correctness is unaffected.
    /// </remarks>
    public ReadOnlyMemory<byte> GetOrEncode(Func<object, ReadOnlyMemory<byte>> encode)
        => _encoded ??= encode(Item);
}
```

An additive, opt-in observer interface:

```csharp
/// <summary>
///     Optional extension of <see cref="IUntypedStreamObserver"/> for observers that serialize the
///     item. Fan-out routes through <see cref="OnNextSharedAsync"/> so encoding is shared across all
///     such observers on the same stream.
/// </summary>
public interface ISharedEncodingStreamObserver : IUntypedStreamObserver
{
    Task OnNextSharedAsync(SharedStreamItem item, StreamSequenceToken? token);
}
```

These types carry **no dynamic code** — `GetOrEncode` only invokes a delegate; the delegate's body
(which does the reflection-based encode) lives in `Quark.Runtime` behind its existing AOT guards.

### 2. `GatewayClientSubscription` — `Quark.Runtime`

`GatewayClientSubscription` implements `ISharedEncodingStreamObserver`. The existing serialization
logic (`GetType()` → `TryGetGeneralizedCodec` → `CodecWriter` → `WriteField`) moves into a local
`Encode(object) : ReadOnlyMemory<byte>` function. The dynamic-code / trim guards
(`RuntimeFeature.IsDynamicCodeSupported` check + `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`)
move onto `OnNextSharedAsync`.

```csharp
[RequiresDynamicCode(/* existing message */)]
[RequiresUnreferencedCode(/* existing message */)]
public Task OnNextSharedAsync(SharedStreamItem item, StreamSequenceToken? token)
{
    if (!RuntimeFeature.IsDynamicCodeSupported)
        throw new NotSupportedException(/* existing message */);

    ReadOnlyMemory<byte> bytes = item.GetOrEncode(Encode);
    return _push(bytes, token);

    ReadOnlyMemory<byte> Encode(object obj)
    {
        Type itemType = obj.GetType();
        IGeneralizedCodec codec = _codecs.TryGetGeneralizedCodec(itemType)
            ?? throw new InvalidOperationException($"No IGeneralizedCodec registered for {itemType.Name}");
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new CodecWriter(buffer);
        codec.WriteField(writer, 0, itemType, obj);
        return buffer.WrittenMemory;
    }
}
```

The existing `OnNextAsync(object item, StreamSequenceToken? token)` remains for callers that hold the
interface directly, delegating to keep one encode path:

```csharp
public Task OnNextAsync(object item, StreamSequenceToken? token)
    => OnNextSharedAsync(new SharedStreamItem(item), token);
```

Because all `GatewayClientSubscription` instances on a stream share one `ICodecProvider`, whichever
runs first in the fan-out encodes and memoizes on the shared `SharedStreamItem`; the rest reuse it.

### 3. `StreamSubscriptionRegistry` — `Quark.Streaming.InMemory`

Add a private helper that fans out concurrently while preserving the `AggregateException` contract:

```csharp
private static async Task FanOutAsync(List<Task> tasks)
{
    var all = Task.WhenAll(tasks);
    try
    {
        await all.ConfigureAwait(false);
    }
    catch when (all.Exception is not null)
    {
        // Task.WhenAll aggregates every failure into all.Exception; awaiting would surface only the
        // first. Rethrow the flattened AggregateException to match the prior contract.
        throw all.Exception;
    }
}
```

`PublishAsync<T>` after the implicit-activation step:

- **Typed `_subs`** — snapshot under `lock` (unchanged), then:
  ```csharp
  var tasks = new List<Task>(snapshot.Count);
  foreach (Subscription sub in snapshot)
      tasks.Add(sub.OnNext(item!, token).AsTask());
  await FanOutAsync(tasks).ConfigureAwait(false);
  ```
- **Untyped `_untyped`** — snapshot under `lock` (unchanged), then build **one** `SharedStreamItem`
  and route each observer by capability:
  ```csharp
  var shared = new SharedStreamItem(item!);
  var tasks = new List<Task>(snapshot.Count);
  foreach (var (_, obs) in snapshot)
      tasks.Add(obs is ISharedEncodingStreamObserver enc
          ? enc.OnNextSharedAsync(shared, token)
          : obs.OnNextAsync(item!, token));
  await FanOutAsync(tasks).ConfigureAwait(false);
  ```

`PublishErrorAsync` and `PublishCompletedAsync` build a `List<Task>` from
`sub.OnError(ex).AsTask()` / `sub.OnCompleted().AsTask()` over the typed snapshot and call
`FanOutAsync`.

### 4. Why "encode once" needs no lock

`Task.WhenAll(tasks)` where `tasks` is built by a `foreach` that calls each observer's method
synchronously: each `OnNextSharedAsync` runs on the calling thread up to its first `await`. The encode
(`item.GetOrEncode(Encode)`) executes in that synchronous prefix, before `await _push`. So the first
gateway observer fully encodes and memoizes before the second observer is even invoked. Concurrency
overlaps only the async `_push` calls. The lock-free `??=` is therefore safe, and the memoization
invariant is documented on `SharedStreamItem.GetOrEncode`.

## Semantics preservation

| Property | Before | After |
|---|---|---|
| Backpressure | publisher awaits all deliveries | unchanged — `FanOutAsync` awaits `Task.WhenAll` |
| Error propagation | `AggregateException` of all failures | unchanged — `FanOutAsync` rethrows `all.Exception` |
| Per-subscriber FIFO | publisher awaits `PublishAsync(A)` before `(B)`; one call per sub per publish | unchanged |
| Registry codec-independence | registry never touches a codec | unchanged — constructs opaque `SharedStreamItem` |
| AOT/trim | dynamic encode behind `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` | unchanged — guards move to `OnNextSharedAsync`; new abstractions carry no dynamic code |

## Testing

New unit tests (in `tests/Quark.Tests.Unit/Streaming/`):

1. **Concurrent fan-out / no head-of-line** — subscribe two observers; gate one on a
   `TaskCompletionSource` that never (or lately) completes, the other returns immediately. Assert the
   fast observer's `OnNextAsync` has been entered/completed while the slow one is still pending, i.e.
   the publish does not serialize the fast delivery behind the slow one.
2. **Serialize-once** — register N gateway subscriptions on one stream backed by a counting
   `IGeneralizedCodec` / `ICodecProvider`; publish one item; assert exactly one `WriteField` call.
3. **Error aggregation** — two observers that throw; assert `PublishAsync` throws an
   `AggregateException` containing both.
4. **Mixed untyped observers** — one `ISharedEncodingStreamObserver` (gateway) and one plain
   `IUntypedStreamObserver`; assert the plain one receives the raw `object` and the gateway one
   receives correctly-encoded bytes.

Regression: `UntypedSubscriptionTests`, `GatewayClientSubscriptionTableTests`, and
`StreamingIntegrationTests` must remain green. Full suite: `dotnet test Quark.slnx`.

## Files touched

- `src/Quark.Streaming.Abstractions/` — **new** `SharedStreamItem.cs`, `ISharedEncodingStreamObserver.cs`.
- `src/Quark.Runtime/GatewayClientSubscription.cs` — implement `ISharedEncodingStreamObserver`; move
  encode into a local function; `OnNextAsync` delegates.
- `src/Quark.Streaming.InMemory/StreamSubscriptionRegistry.cs` — `FanOutAsync` helper; convert the
  four fan-out loops.
- `tests/Quark.Tests.Unit/Streaming/` — new tests above.
