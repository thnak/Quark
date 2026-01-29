namespace Quark.Abstractions.Clustering;

/// <summary>
///     Represents a node (silo) in the Quark cluster.
/// </summary>
public sealed class SiloInfo
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SiloInfo" /> class.
    /// </summary>
    public SiloInfo(string siloId, string address, int port, SiloStatus status = SiloStatus.Active)
    {
        SiloId = siloId ?? throw new ArgumentNullException(nameof(siloId));
        Address = address ?? throw new ArgumentNullException(nameof(address));
        Port = port;
        Status = status;
        LastHeartbeat = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     Gets the unique identifier for this silo.
    /// </summary>
    public string SiloId { get; }

    /// <summary>
    ///     Gets the network address of the silo.
    /// </summary>
    public string Address { get; }

    /// <summary>
    ///     Gets the port the silo is listening on.
    /// </summary>
    public int Port { get; }

    /// <summary>
    ///     Gets the current status of the silo.
    /// </summary>
    public SiloStatus Status { get; internal set; }

    /// <summary>
    ///     Gets the timestamp of the last heartbeat from this silo.
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; internal set; }

    /// <summary>
    ///     Gets the endpoint string in the format "address:port".
    /// </summary>
    public string Endpoint => $"{Address}:{Port}";
}

/// <summary>
///     Represents the status of a silo in the cluster.
/// </summary>
public enum SiloStatus
{
    /// <summary>
    ///     The silo is joining the cluster.
    /// </summary>
    Joining,

    /// <summary>
    ///     The silo is active and processing requests.
    /// </summary>
    Active,

    /// <summary>
    ///     The silo is shutting down gracefully.
    /// </summary>
    ShuttingDown,

    /// <summary>
    ///     The silo is dead or unreachable.
    /// </summary>
    Dead
}