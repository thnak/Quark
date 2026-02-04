namespace Quark.Clustering.Redis;

/// <summary>
/// Event arguments for connection health degradation.
/// </summary>
public sealed class ConnectionHealthDegradedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionHealthDegradedEventArgs"/> class.
    /// </summary>
    public ConnectionHealthDegradedEventArgs(ConnectionHealthStatus status)
    {
        Status = status;
    }

    /// <summary>
    /// Gets the current health status.
    /// </summary>
    public ConnectionHealthStatus Status { get; }
}