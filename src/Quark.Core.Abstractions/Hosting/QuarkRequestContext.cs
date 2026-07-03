namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Ambient, async-flow-scoped carrier for per-call request metadata.
///     Read by outbound <see cref="IGrainCallInvoker" /> implementations and stamped onto
///     <c>MessageEnvelope</c> headers. Quark-native analogue of Orleans' <c>RequestContext</c>.
/// </summary>
public static class QuarkRequestContext
{
    private static readonly AsyncLocal<string?> _idempotencyKey = new();

    /// <summary>The idempotency key for the current async flow, or <c>null</c>.</summary>
    public static string? IdempotencyKey => _idempotencyKey.Value;

    /// <summary>
    ///     Sets the idempotency key for calls made inside the returned scope.
    ///     The <see cref="AsyncLocal{T}" /> slot is restored to its previous value on
    ///     <see cref="IDisposable.Dispose" />, so the key does not leak to sibling or parent flows.
    /// </summary>
    public static IDisposable WithIdempotencyKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        string? previous = _idempotencyKey.Value;
        _idempotencyKey.Value = key;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _idempotencyKey.Value = previous;
            }
        }
    }
}
