namespace Quark.Clustering.Redis;

/// <summary>
/// Event arguments for connection restoration.
/// </summary>
public sealed class ConnectionRestoredEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionRestoredEventArgs"/> class.
    /// </summary>
    public ConnectionRestoredEventArgs(System.Net.EndPoint? endPoint, int previousFailureCount)
    {
        EndPoint = endPoint;
        PreviousFailureCount = previousFailureCount;
    }

    /// <summary>
    /// Gets the endpoint that was restored.
    /// </summary>
    public System.Net.EndPoint? EndPoint { get; }

    /// <summary>
    /// Gets the number of failures before restoration.
    /// </summary>
    public int PreviousFailureCount { get; }
}