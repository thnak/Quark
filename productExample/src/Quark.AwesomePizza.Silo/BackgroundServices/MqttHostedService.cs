namespace Quark.AwesomePizza.Silo.BackgroundServices;

/// <summary>
/// Hosted service for MQTT integration.
/// </summary>
internal class MqttHostedService : IHostedService
{
    private readonly MqttService _mqttService;

    public MqttHostedService(MqttService mqttService)
    {
        _mqttService = mqttService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _mqttService.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _mqttService.StopAsync();
    }
}