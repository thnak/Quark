namespace Quark.AwesomePizza.Shared.Client;

/// <summary>
/// Temporary IClusterClient interface for actor proxy pattern.
/// This is a placeholder until the full IClusterClient is implemented in Quark.Client.
/// See: productExample/implements/tasks/FEATURE-REQUEST-IClusterClient.md
/// </summary>
public interface IClusterClient
{
    /// <summary>
    /// Gets a proxy reference to an actor of type T with the specified ID.
    /// The actor must implement the interface T.
    /// </summary>
    /// <typeparam name="T">Actor interface type</typeparam>
    /// <param name="actorId">Unique actor identifier</param>
    /// <returns>Actor proxy that implements T</returns>
    T GetActor<T>(string actorId) where T : class;
    
    /// <summary>
    /// Connects to the actor cluster.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnects from the actor cluster.
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Checks if the client is currently connected to the cluster.
    /// </summary>
    bool IsConnected { get; }
}
