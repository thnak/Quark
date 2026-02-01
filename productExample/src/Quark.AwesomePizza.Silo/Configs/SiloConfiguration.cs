namespace Quark.AwesomePizza.Silo.Configs;

/// <summary>
/// Silo configuration.
/// </summary>
public record SiloConfiguration
{
    public string SiloId { get; init; } = string.Empty;
    public string RedisHost { get; init; } = string.Empty;
    public string RedisPort { get; init; } = string.Empty;
    public string MqttHost { get; init; } = string.Empty;
    public int MqttPort { get; init; }
}