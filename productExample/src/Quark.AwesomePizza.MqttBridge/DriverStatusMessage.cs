namespace Quark.AwesomePizza.MqttBridge;

/// <summary>
/// Message payload for driver status updates.
/// </summary>
internal class DriverStatusMessage
{
    public string Status { get; set; } = string.Empty;
}