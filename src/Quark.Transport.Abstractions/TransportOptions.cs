namespace Quark.Transport.Abstractions;

/// <summary>
///     Options governing transport-level behaviour (timeouts, buffer sizes, etc.).
/// </summary>
public class TransportOptions
{
    /// <summary>
    ///     Maximum number of concurrent inbound connections per listener.
    ///     Default: 1024.
    /// </summary>
    public int MaxConnections { get; set; } = 1024;

    /// <summary>
    ///     Size in bytes of the initial receive buffer per connection.
    ///     Default: 16 KB.
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 16 * 1024;// TODO did not implemented or used in any elsewhere

    /// <summary>
    ///     Size in bytes of the initial send buffer per connection.
    ///     Default: 16 KB.
    /// </summary>
    public int SendBufferSize { get; set; } = 16 * 1024;// TODO did not implemented or used in any elsewhere

    /// <summary>
    ///     Idle connection timeout. Default: 5 minutes.
    ///     Set to <see cref="Timeout.InfiniteTimeSpan" /> to disable.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);// TODO did not implemented or used in any elsewhere

    /// <summary>
    ///     Connection attempt timeout.  Default: 30 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Maximum serialized message frame size in bytes.
    ///     Default: 100 MB.
    /// </summary>
    public int MaxMessageBytes { get; set; } = 100 * 1024 * 1024;
}
