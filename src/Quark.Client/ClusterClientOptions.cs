namespace Quark.Client;

/// <summary>
/// Configuration options for ClusterClient.
/// </summary>
public sealed class ClusterClientOptions
{
    /// <summary>
    /// Gets or sets the client ID. If not specified, a unique ID will be generated.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the timeout for connection attempts. Defaults to 10 seconds.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the timeout for request operations. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts. Defaults to 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between retry attempts. Defaults to 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
