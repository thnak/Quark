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