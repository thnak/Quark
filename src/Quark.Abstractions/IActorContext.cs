namespace Quark.Abstractions;

/// <summary>
///     Provides contextual information for the current actor execution.
/// </summary>
public interface IActorContext
{
    /// <summary>
    ///     Gets the current actor ID.
    /// </summary>
    string ActorId { get; }

    /// <summary>
    ///     Gets the correlation ID for distributed tracing.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    ///     Gets the request ID for the current execution.
    /// </summary>
    string? RequestId { get; }

    /// <summary>
    ///     Gets the metadata associated with the current execution.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    ///     Sets a metadata value for the current execution.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    void SetMetadata(string key, object value);

    /// <summary>
    ///     Gets a metadata value for the current execution.
    /// </summary>
    /// <typeparam name="T">The type of the metadata value.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <returns>The metadata value, or default if not found.</returns>
    T? GetMetadata<T>(string key);
}