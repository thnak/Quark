using Quark.Abstractions;
using Quark.Networking.Abstractions;

namespace Quark.Hosting;

/// <summary>
/// Wraps a QuarkEnvelope as an IActorMessage for mailbox processing.
/// This enables sequential message processing per actor through the mailbox system.
/// </summary>
internal sealed class ActorEnvelopeMessage : IActorMessage
{
    private readonly QuarkEnvelope _envelope;
    private readonly TaskCompletionSource<QuarkEnvelope> _responseSource;

    public ActorEnvelopeMessage(QuarkEnvelope envelope)
    {
        _envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        _responseSource = new TaskCompletionSource<QuarkEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <inheritdoc />
    public string MessageId => _envelope.MessageId;

    /// <inheritdoc />
    public string CorrelationId => _envelope.CorrelationId;

    /// <inheritdoc />
    public DateTimeOffset Timestamp => _envelope.Timestamp;

    /// <summary>
    /// Gets the wrapped envelope.
    /// </summary>
    public QuarkEnvelope Envelope => _envelope;

    /// <summary>
    /// Gets the task that completes when a response is available.
    /// </summary>
    public Task<QuarkEnvelope> ResponseTask => _responseSource.Task;

    /// <summary>
    /// Sets the response envelope.
    /// </summary>
    public void SetResponse(QuarkEnvelope response)
    {
        _responseSource.TrySetResult(response);
    }

    /// <summary>
    /// Sets an exception if processing failed.
    /// </summary>
    public void SetException(Exception exception)
    {
        _responseSource.TrySetException(exception);
    }
}
