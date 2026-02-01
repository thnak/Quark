using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.AwesomePizza.MqttBridge;

/// <summary>
/// MQTT Bridge Service - Connects IoT devices to Quark actors via MQTT protocol.
/// Uses MQTTnet library for robust MQTT client with automatic reconnection.
/// </summary>
internal class Program
{
    private static IManagedMqttClient? _mqttClient;
    private static IActorFactory? _actorFactory;
    private static readonly Dictionary<string, IActor> _activeActors = new();
    private static readonly CancellationTokenSource _cts = new();

    private static string _mqttBrokerHost = "localhost";
    private static int _mqttBrokerPort = 1883;
    private static string _clientId = $"awesomepizza-bridge-{Guid.NewGuid():N}";

    private static async Task Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Awesome Pizza - MQTT Bridge Service                ║");
        Console.WriteLine("║       Real-time IoT Integration with MQTTnet             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Parse command-line arguments
        ParseArguments(args);

        Console.WriteLine($"🔌 MQTT Broker: {_mqttBrokerHost}:{_mqttBrokerPort}");
        Console.WriteLine($"🆔 Client ID: {_clientId}");
        Console.WriteLine($"🚀 Started at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();

        // Initialize actor factory
        _actorFactory = new ActorFactory();
        Console.WriteLine("✅ Actor factory initialized");

        // Create and configure MQTT client
        await InitializeMqttClientAsync();

        // Register shutdown handler
        Console.CancelKeyPress += async (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("🛑 Shutting down MQTT bridge...");
            await ShutdownAsync();
        };

        Console.WriteLine();
        Console.WriteLine("📋 Subscribed Topics:");
        Console.WriteLine("   • pizza/drivers/+/location    - Driver GPS updates");
        Console.WriteLine("   • pizza/drivers/+/status      - Driver status changes");
        Console.WriteLine("   • pizza/kitchen/+/oven        - Oven telemetry");
        Console.WriteLine("   • pizza/kitchen/+/alerts      - Equipment alerts");
        Console.WriteLine("   • pizza/orders/+/events       - Order events");
        Console.WriteLine();
        Console.WriteLine("💡 Tip: Use mosquitto_pub to test:");
        Console.WriteLine("   mosquitto_pub -t \"pizza/drivers/driver-1/location\" -m '{\"lat\":40.7128,\"lon\":-74.0060}'");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to exit");
        Console.WriteLine();

        // Keep the service running
        try
        {
            await Task.Delay(Timeout.Infinite, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }

        Console.WriteLine("👋 MQTT bridge shutdown complete");
    }

    private static void ParseArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host" when i + 1 < args.Length:
                    _mqttBrokerHost = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    _mqttBrokerPort = int.Parse(args[++i]);
                    break;
                case "--client-id" when i + 1 < args.Length:
                    _clientId = args[++i];
                    break;
            }
        }
    }

    private static async Task InitializeMqttClientAsync()
    {
        // Create MQTT factory
        var factory = new MqttFactory();

        // Create managed client (handles reconnection automatically)
        _mqttClient = factory.CreateManagedMqttClient();

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

        Console.WriteLine("⏳ Connecting to MQTT broker...");
    }

    private static async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        Console.WriteLine($"✅ Connected to MQTT broker");

        // Subscribe to all topics
        var subscriptions = new List<MQTTnet.Packets.MqttTopicFilter>
        {
            new MqttTopicFilterBuilder().WithTopic("pizza/drivers/+/location").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            new MqttTopicFilterBuilder().WithTopic("pizza/drivers/+/status").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            new MqttTopicFilterBuilder().WithTopic("pizza/kitchen/+/oven").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            new MqttTopicFilterBuilder().WithTopic("pizza/kitchen/+/alerts").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build(),
            new MqttTopicFilterBuilder().WithTopic("pizza/orders/+/events").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build()
        };

        await _mqttClient!.SubscribeAsync(subscriptions);
        Console.WriteLine("✅ Subscribed to all topics");
    }

    private static Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        Console.WriteLine($"⚠️  Disconnected from MQTT broker");
        if (args.Exception != null)
        {
            Console.WriteLine($"   Reason: {args.Exception.Message}");
        }
        Console.WriteLine("   Will attempt to reconnect...");
        return Task.CompletedTask;
    }

    private static Task OnConnectingFailedAsync(ConnectingFailedEventArgs args)
    {
        Console.WriteLine($"❌ Connection failed: {args.Exception.Message}");
        Console.WriteLine("   Retrying in 5 seconds...");
        return Task.CompletedTask;
    }

    private static async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

        Console.WriteLine($"📩 Message received on topic: {topic}");

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
            else
            {
                Console.WriteLine($"⚠️  Unknown topic pattern: {topic}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing message: {ex.Message}");
        }
    }

    private static  Task HandleDriverMessageAsync(string topic, string payload)
    {
        return Task.CompletedTask;
    }

    private static Task HandleKitchenMessageAsync(string topic, string payload)
    {
        // Parse topic: pizza/kitchen/{kitchenId}/{action}
        var parts = topic.Split('/');
        if (parts.Length != 4)
        {
            Console.WriteLine($"⚠️  Invalid kitchen topic format: {topic}");
            return Task.CompletedTask;
        }

        var kitchenId = parts[2];
        var action = parts[3];

        Console.WriteLine($"   📝 Kitchen telemetry for {kitchenId}: {action}");
        Console.WriteLine($"   Data: {payload}");

        // TODO: Implement kitchen telemetry handling when KitchenActor is ready
        // This would involve parsing oven temperature, cooking timers, etc.

        return Task.CompletedTask;
    }

    private static Task HandleOrderMessageAsync(string topic, string payload)
    {
        // Parse topic: pizza/orders/{orderId}/events
        var parts = topic.Split('/');
        if (parts.Length != 4)
        {
            Console.WriteLine($"⚠️  Invalid order topic format: {topic}");
            return Task.CompletedTask;
        }

        var orderId = parts[2];

        Console.WriteLine($"   📝 Order event for {orderId}");
        Console.WriteLine($"   Data: {payload}");

        // TODO: Implement order event handling if needed
        // This could be used for external order status updates

        return Task.CompletedTask;
    }

    private static async Task<T?> GetOrCreateActorAsync<T>(string actorId) where T : IActor
    {
        if (_actorFactory == null)
            return default;

        if (_activeActors.TryGetValue(actorId, out var existingActor) && existingActor is T typedActor)
        {
            return typedActor;
        }

        var actor = _actorFactory.CreateActor<T>(actorId);
        await actor.OnActivateAsync();
        _activeActors[actorId] = actor;

        Console.WriteLine($"   🆕 Created new actor: {typeof(T).Name} ({actorId})");

        return actor;
    }

    private static async Task ShutdownAsync()
    {
        if (_mqttClient != null)
        {
            Console.WriteLine("   Disconnecting from MQTT broker...");
            await _mqttClient.StopAsync();
            _mqttClient.Dispose();
        }

        // Deactivate all actors
        foreach (var kvp in _activeActors.ToList())
        {
            try
            {
                await kvp.Value.OnDeactivateAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Error deactivating {kvp.Key}: {ex.Message}");
            }
        }

        _activeActors.Clear();
        await _cts.CancelAsync();
    }
}

/// <summary>
/// Message payload for driver location updates.
/// Supports multiple field name formats (lat/latitude, lon/longitude).
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
