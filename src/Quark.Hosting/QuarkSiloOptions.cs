namespace Quark.Hosting;

/// <summary>
/// Configuration options for QuarkSilo.
/// </summary>
public sealed class QuarkSiloOptions
{
    /// <summary>
    /// Gets or sets the silo ID. If not specified, a unique ID will be generated.
    /// </summary>
    public string? SiloId { get; set; }

    /// <summary>
    /// Gets or sets the address this silo listens on. Defaults to localhost.
    /// </summary>
    public string Address { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the port this silo listens on. Defaults to 11111.
    /// </summary>
    public int Port { get; set; } = 11111;

    /// <summary>
    /// Gets or sets the timeout for graceful shutdown. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the heartbeat interval for cluster membership. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets whether to start the reminder tick manager. Defaults to true.
    /// </summary>
    public bool EnableReminders { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to start the stream broker. Defaults to true.
    /// </summary>
    public bool EnableStreaming { get; set; } = true;
}
