namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     The client interface for interacting with a Quark cluster.
///     Extends <see cref="IGrainFactory" /> so callers can obtain grain references directly.
/// </summary>
public interface IClusterClient : IGrainFactory, IAsyncDisposable
{
    /// <summary>Gets a value indicating whether the client is currently connected to the cluster.</summary>
    bool IsInitialized { get; }

    /// <summary>Connects to the cluster. Must be called before using grain factories.</summary>
    Task Connect(Func<Exception, Task>? retryFilter = null);

    /// <summary>Disconnects gracefully from the cluster.</summary>
    Task Close();
}
