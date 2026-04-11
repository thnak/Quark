using System.Net;

namespace Quark.Transport.Abstractions;

/// <summary>
/// A bound listener that accepts inbound <see cref="ITransportConnection"/>s.
/// </summary>
public interface ITransportListener : IAsyncDisposable
{
    /// <summary>The local endpoint this listener is bound to.</summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>Binds the listener to its endpoint.</summary>
    Task BindAsync(CancellationToken cancellationToken = default);

    /// <summary>Waits for and returns the next inbound connection.</summary>
    /// <returns><c>null</c> when the listener has been stopped.</returns>
    ValueTask<ITransportConnection?> AcceptAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops accepting new connections. Existing connections are not affected.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
