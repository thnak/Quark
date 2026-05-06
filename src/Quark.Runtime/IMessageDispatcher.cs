using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Routes incoming message envelopes to the appropriate grain activation.
/// </summary>
public interface IMessageDispatcher
{
    /// <summary>Processes a message envelope and returns an optional response envelope.</summary>
    Task<MessageEnvelope?> DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);
}