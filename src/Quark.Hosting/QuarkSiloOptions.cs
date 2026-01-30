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
    /// Allows in-flight operations to complete before forcing shutdown.
    /// Part of Phase 10.1.1 (Zero Downtime & Rolling Upgrades).
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

    /// <summary>
    /// Gets or sets whether to enable live actor migration during rolling upgrades. Defaults to false.
    /// When enabled, actors can be migrated to other silos during shutdown.
    /// Part of Phase 10.1.1 (Zero Downtime & Rolling Upgrades - IMPLEMENTED).
    /// </summary>
    public bool EnableLiveMigration { get; set; } = false;

    /// <summary>
    /// Gets or sets the timeout for actor migration operations. Defaults to 30 seconds.
    /// Only applies when EnableLiveMigration is true.
    /// Part of Phase 10.1.1 (Zero Downtime & Rolling Upgrades - IMPLEMENTED).
    /// </summary>
    public TimeSpan MigrationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum number of concurrent actor migrations. Defaults to 10.
    /// Only applies when EnableLiveMigration is true.
    /// Part of Phase 10.1.1 (Zero Downtime & Rolling Upgrades - IMPLEMENTED).
    /// </summary>
    public int MaxConcurrentMigrations { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether to enable version-aware placement. Defaults to false.
    /// When enabled, actors are preferentially placed on silos with matching assembly versions.
    /// Part of Phase 10.1.1 (Zero Downtime & Rolling Upgrades - PLANNED).
    /// </summary>
    public bool EnableVersionAwarePlacement { get; set; } = false;

    /// <summary>
    /// Gets or sets the assembly version for this silo. If not specified, will be auto-detected.
    /// Used for version-aware placement during rolling upgrades.
    /// Part of Phase 10.1.1 (Zero Downtime & Rolling Upgrades - PLANNED).
    /// </summary>
    public string? AssemblyVersion { get; set; }
}
