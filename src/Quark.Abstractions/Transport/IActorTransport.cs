namespace Quark.Abstractions.Transport;

/// <summary>
///     Provides transport-level communication for remote actor invocations.
/// </summary>
public interface IActorTransport : IDisposable
{
    /// <summary>
    ///     Gets the local endpoint this transport is listening on.
    /// </summary>
    string LocalEndpoint { get; }

    /// <summary>
    ///     Invokes a method on a remote actor.
    /// </summary>
    /// <param name="targetEndpoint">The endpoint of the target silo.</param>
    /// <param name="request">The invocation request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The invocation response.</returns>
    Task<ActorInvocationResponse> InvokeAsync(
        string targetEndpoint,
        ActorInvocationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Starts the transport and begins listening for incoming requests.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops the transport and stops listening for incoming requests.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}