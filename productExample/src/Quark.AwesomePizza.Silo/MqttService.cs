using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Silo;

/// <summary>
/// MQTT Service integrated into Silo for real-time IoT updates.
/// Receives MQTT messages and updates actors directly within the Silo.
/// </summary>
internal class MqttService
{
    private readonly IActorFactory _actorFactory;
    private readonly Dictionary<string, IActor> _activeActors;
    private readonly IManagedMqttClient _mqttClient;
    private readonly string _mqttBrokerHost;
    private readonly int _mqttBrokerPort;
    private readonly string _clientId;

    public MqttService(
        IActorFactory actorFactory,
        Dictionary<string, IActor> activeActors,
        string mqttBrokerHost = "localhost",
        int mqttBrokerPort = 1883,
        string? clientId = null)
    {
        ArgumentNullException.ThrowIfNull(actorFactory);
        ArgumentNullException.ThrowIfNull(activeActors);

        _actorFactory = actorFactory;
        _activeActors = activeActors;
        _mqttBrokerHost = mqttBrokerHost;
        _mqttBrokerPort = mqttBrokerPort;
        _clientId = clientId ?? $"awesomepizza-silo-{Guid.NewGuid():N}";

        var factory = new MqttFactory();
        _mqttClient = factory.CreateManagedMqttClient();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Configure managed client options
        var options = new ManagedMqttClientOptionsBuilder()
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .WithClientOptions(new MqttClientOptionsBuilder()
                .WithTcpServer(_mqttBrokerHost, _mqttBrokerPort)
                .WithClientId(_clientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                .Build())
            .Build();

        // Set up event handlers
        _mqttClient.ConnectedAsync += OnConnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
        _mqttClient.ConnectingFailedAsync += OnConnectingFailedAsync;
        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        // Start the client
        await _mqttClient.StartAsync(options);

        Console.WriteLine($"🔌 MQTT Client ID: {_clientId}");
        Console.WriteLine($"🔌 MQTT Broker: {_mqttBrokerHost}:{_mqttBrokerPort}");
        Console.WriteLine("⏳ Connecting to MQTT broker...");
    }

    public async Task StopAsync()
    {
        if (_mqttClient != null)
        {
            Console.WriteLine("🛑 Disconnecting from MQTT broker...");
            await _mqttClient.StopAsync();
            _mqttClient.Dispose();
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        Console.WriteLine("✅ MQTT: Connected to broker");

        // Subscribe to all topics
        var subscriptions = new List<MQTTnet.Packets.MqttTopicFilter>
        {
            new MqttTopicFilterBuilder().WithTopic("pizza/drivers/+/location").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            new MqttTopicFilterBuilder().WithTopic("pizza/drivers/+/status").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            new MqttTopicFilterBuilder().WithTopic("pizza/kitchen/+/oven").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            new MqttTopicFilterBuilder().WithTopic("pizza/kitchen/+/alerts").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            new MqttTopicFilterBuilder().WithTopic("pizza/orders/+/events").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build()
        };

        await _mqttClient.SubscribeAsync(subscriptions);
        Console.WriteLine("✅ MQTT: Subscribed to topics");
        Console.WriteLine("   • pizza/drivers/+/location");
        Console.WriteLine("   • pizza/drivers/+/status");
        Console.WriteLine("   • pizza/kitchen/+/oven");
        Console.WriteLine("   • pizza/kitchen/+/alerts");
        Console.WriteLine("   • pizza/orders/+/events");
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        Console.WriteLine("⚠️  MQTT: Disconnected from broker");
        if (args.Exception != null)
        {
            Console.WriteLine($"   Reason: {args.Exception.Message}");
        }
        Console.WriteLine("   Will attempt to reconnect...");
        return Task.CompletedTask;
    }

    private Task OnConnectingFailedAsync(ConnectingFailedEventArgs args)
    {
        Console.WriteLine($"❌ MQTT: Connection failed: {args.Exception.Message}");
        Console.WriteLine("   Retrying in 5 seconds...");
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

        Console.WriteLine($"📩 MQTT: {topic}");

        try
        {
            // Route message to appropriate handler based on topic pattern
            if (topic.StartsWith("pizza/drivers/"))
            {
                await HandleDriverMessageAsync(topic, payload);
            }
            else if (topic.StartsWith("pizza/kitchen/"))
            {
                await HandleKitchenMessageAsync(topic, payload);
            }
            else if (topic.StartsWith("pizza/orders/"))
            {
                await HandleOrderMessageAsync(topic, payload);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ MQTT: Error processing message: {ex.Message}");
        }
    }

    private async Task HandleDriverMessageAsync(string topic, string payload)
    {
        // Parse topic: pizza/drivers/{driverId}/{action}
        var parts = topic.Split('/');
        if (parts.Length != 4)
        {
            Console.WriteLine($"⚠️  MQTT: Invalid driver topic format: {topic}");
            return;
        }

        var driverId = parts[2];
        var action = parts[3];

        // Get or create driver actor
        var driverActor = await GetOrCreateActorAsync<DriverActor>(driverId);
        if (driverActor == null)
        {
            Console.WriteLine($"❌ MQTT: Failed to get/create driver actor: {driverId}");
            return;
        }

        try
        {
            switch (action)
            {
                case "location":
                    var locationData = JsonSerializer.Deserialize<DriverLocationMessage>(payload);
                    if (locationData != null)
                    {
                        var lat = locationData.Lat ?? locationData.Latitude ?? 0.0;
                        var lon = locationData.Lon ?? locationData.Longitude ?? 0.0;
                        
                        await driverActor.UpdateLocationAsync(lat, lon, locationData.Timestamp ?? DateTime.UtcNow);
                        Console.WriteLine($"   ✅ Updated location for {driverId}: ({lat:F4}, {lon:F4})");
                    }
                    break;

                case "status":
                    var statusData = JsonSerializer.Deserialize<DriverStatusMessage>(payload);
                    if (statusData != null && Enum.TryParse<DriverStatus>(statusData.Status, true, out var status))
                    {
                        await driverActor.UpdateStatusAsync(status);
                        Console.WriteLine($"   ✅ Updated status for {driverId}: {status}");
                    }
                    break;

                default:
                    Console.WriteLine($"⚠️  MQTT: Unknown driver action: {action}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"❌ MQTT: JSON parsing error: {ex.Message}");
        }
    }

    private Task HandleKitchenMessageAsync(string topic, string payload)
    {
        // Parse topic: pizza/kitchen/{kitchenId}/{action}
        var parts = topic.Split('/');
        if (parts.Length != 4)
        {
            Console.WriteLine($"⚠️  MQTT: Invalid kitchen topic format: {topic}");
            return Task.CompletedTask;
        }

        var kitchenId = parts[2];
        var action = parts[3];

        Console.WriteLine($"   📝 Kitchen telemetry for {kitchenId}: {action}");
        
        // TODO: Implement kitchen telemetry handling when KitchenActor is ready
        // This would involve parsing oven temperature, cooking timers, etc.

        return Task.CompletedTask;
    }

    private Task HandleOrderMessageAsync(string topic, string payload)
    {
        // Parse topic: pizza/orders/{orderId}/events
        var parts = topic.Split('/');
        if (parts.Length != 4)
        {
            Console.WriteLine($"⚠️  MQTT: Invalid order topic format: {topic}");
            return Task.CompletedTask;
        }

        var orderId = parts[2];
        Console.WriteLine($"   📝 Order event for {orderId}");
        
        // TODO: Implement order event handling if needed

        return Task.CompletedTask;
    }

    private async Task<T?> GetOrCreateActorAsync<T>(string actorId) where T : IActor
    {
        if (_activeActors.TryGetValue(actorId, out var existingActor) && existingActor is T typedActor)
        {
            return typedActor;
        }

        var actor = _actorFactory.CreateActor<T>(actorId);
        await actor.OnActivateAsync();
        _activeActors[actorId] = actor;

        Console.WriteLine($"   🆕 Created actor: {typeof(T).Name} ({actorId})");

        return actor;
    }
}

/// <summary>
/// Message payload for driver location updates.
/// </summary>
internal class DriverLocationMessage
{
    public double? Lat { get; set; }
    public double? Latitude { get; set; }
    public double? Lon { get; set; }
    public double? Longitude { get; set; }
    public DateTime? Timestamp { get; set; }
}

/// <summary>
/// Message payload for driver status updates.
/// </summary>
internal class DriverStatusMessage
{
    public string Status { get; set; } = string.Empty;
}
