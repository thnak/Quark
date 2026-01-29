using System.Collections.Concurrent;
using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
/// Provides contextual information for actor execution with support for async flow.
/// Uses AsyncLocal to propagate context across async boundaries.
/// </summary>
public sealed class ActorContext : IActorContext
{
    private static readonly AsyncLocal<ActorContext?> _current = new();
    private readonly ConcurrentDictionary<string, object> _metadata = new();

    /// <summary>
    /// Gets the current actor context for the executing async flow.
    /// </summary>
    public static ActorContext? Current => _current.Value;

    /// <summary>
    /// Sets the current actor context for the executing async flow.
    /// </summary>
    internal static void SetCurrent(ActorContext? context)
    {
        _current.Value = context;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorContext"/> class.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="correlationId">The correlation ID for distributed tracing.</param>
    /// <param name="requestId">The request ID for the current execution.</param>
    public ActorContext(string actorId, string? correlationId = null, string? requestId = null)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        CorrelationId = correlationId ?? Guid.NewGuid().ToString();
        RequestId = requestId ?? Guid.NewGuid().ToString();
    }

    /// <inheritdoc />
    public string ActorId { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <inheritdoc />
    public string? RequestId { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Metadata => _metadata;

    /// <inheritdoc />
    public void SetMetadata(string key, object value)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        _metadata[key] = value;
    }

    /// <inheritdoc />
    public T? GetMetadata<T>(string key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (_metadata.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }

        return default;
    }

    /// <summary>
    /// Creates a new scope for the current context.
    /// </summary>
    /// <returns>A disposable scope that restores the previous context when disposed.</returns>
    public static IDisposable CreateScope(ActorContext context)
    {
        return new ActorContextScope(context);
    }

    private sealed class ActorContextScope : IDisposable
    {
        private readonly ActorContext? _previousContext;

        public ActorContextScope(ActorContext context)
        {
            _previousContext = Current;
            SetCurrent(context);
        }

        public void Dispose()
        {
            SetCurrent(_previousContext);
        }
    }
}
