namespace Quark.Abstractions;

/// <summary>
/// Configuration options for actor logging behavior.
/// </summary>
public sealed class ActorLoggingOptions
{
    /// <summary>
    /// Gets or sets whether to use actor-specific log scopes.
    /// When enabled, all logs within an actor will include the ActorId and ActorType in the scope.
    /// Default is true.
    /// </summary>
    public bool UseActorScopes { get; set; } = true;

    /// <summary>
    /// Gets or sets the global log sampling configuration.
    /// When set, applies sampling to all actors unless overridden per actor type.
    /// </summary>
    public LogSamplingConfiguration? GlobalSamplingConfiguration { get; set; }

    /// <summary>
    /// Gets or sets per-actor-type sampling configurations.
    /// These override global sampling settings for specific actor types.
    /// </summary>
    public Dictionary<string, LogSamplingConfiguration> ActorTypeSamplingConfigurations { get; set; } = new();

    /// <summary>
    /// Gets the effective sampling configuration for a given actor type.
    /// </summary>
    /// <param name="actorTypeName">The actor type name.</param>
    /// <returns>The effective sampling configuration, or null if no sampling.</returns>
    public LogSamplingConfiguration? GetSamplingConfiguration(string actorTypeName)
    {
        if (ActorTypeSamplingConfigurations.TryGetValue(actorTypeName, out var actorConfig))
        {
            return actorConfig;
        }

        return GlobalSamplingConfiguration;
    }
}

/// <summary>
/// Configuration for log sampling to reduce log volume for high-frequency actors.
/// </summary>
public sealed class LogSamplingConfiguration
{
    /// <summary>
    /// Gets or sets whether sampling is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the sampling rate (0.0 to 1.0).
    /// 1.0 = log everything, 0.1 = log 10% of events, 0.01 = log 1% of events.
    /// Default is 0.1 (10%).
    /// </summary>
    public double SamplingRate { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the minimum log level to apply sampling to.
    /// Values: 0=Trace, 1=Debug, 2=Information, 3=Warning, 4=Error, 5=Critical, 6=None.
    /// Logs below this level are always logged (not sampled).
    /// Default is 2 (Information, so Debug and Trace are always logged).
    /// </summary>
    public int MinimumLevelForSampling { get; set; } = 2; // Information

    /// <summary>
    /// Gets or sets whether errors and critical logs should always be logged regardless of sampling.
    /// Default is true (errors are never sampled).
    /// </summary>
    public bool AlwaysLogErrors { get; set; } = true;

    /// <summary>
    /// Determines if a log should be sampled based on the sampling rate.
    /// </summary>
    /// <returns>True if the log should be written, false if it should be skipped.</returns>
    public bool ShouldLog()
    {
        if (!Enabled || SamplingRate >= 1.0)
            return true;

        if (SamplingRate <= 0.0)
            return false;

        return Random.Shared.NextDouble() < SamplingRate;
    }

    /// <summary>
    /// Determines if a log at the specified level should be sampled.
    /// </summary>
    /// <param name="logLevel">The log level to check (0=Trace, 1=Debug, 2=Information, 3=Warning, 4=Error, 5=Critical).</param>
    /// <returns>True if the log should be written, false if it should be skipped.</returns>
    public bool ShouldLog(int logLevel)
    {
        // Always log errors/critical if configured (4=Error, 5=Critical)
        if (AlwaysLogErrors && logLevel >= 4)
            return true;

        // Don't sample logs below minimum level
        if (logLevel < MinimumLevelForSampling)
            return true;

        return ShouldLog();
    }
}
