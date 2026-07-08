using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Hosting;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Codecs;
using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Quark.Transport.Tcp;
using Xunit;

namespace Quark.Tests.Integration;

/// <summary>
///     End-to-end test: a TCP gateway client subscribes to a stream, receives pushed items,
///     and can stop delivery by unsubscribing.
///     Uses real loopback TCP sockets — placed in the serialised gateway collection to
///     prevent port-reuse races.
/// </summary>
[Collection("GatewayTests")]
[Trait("category", "integration")]
public sealed class ClientStreamingIntegrationTests : IAsyncLifetime
{
    // Distinct ports — offset from GatewayIntegrationTests (11200 / 30100) to avoid conflict.
    private const int SiloPort    = 11201;
    private const int GatewayPort = 31200;

    private readonly string _clusterId = $"cs-test-{Guid.NewGuid():N}";

    private IHost _siloHost  = null!;
    private IHost _clientHost = null!;

    public async Task InitializeAsync()
    {
        _siloHost = Host.CreateDefaultBuilder()
            .UseQuark(silo =>
            {
                silo.Services.AddQuarkRuntime();
                silo.Services.AddTcpTransport();
                silo.Services.AddMemoryStreams("test");
                silo.Services.AddStreamableCodec<string?, StringCodec>();
                silo.UseLocalhostClustering(
                    siloPort: SiloPort,
                    gatewayPort: GatewayPort,
                    clusterId: _clusterId);
                // Silo-side client used by internal services (grains calling grains).
                silo.Services.AddLocalClusterClient();
            })
            .Build();

        _clientHost = Host.CreateDefaultBuilder()
            .UseQuarkClient(client =>
            {
                client.UseLocalhostGateway(GatewayPort);
                client.AddTcpClientStreams("test");
            })
            .Build();

        await _siloHost.StartAsync();
        await _clientHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _clientHost.StopAsync();
        await _siloHost.StopAsync();
        _clientHost.Dispose();
        _siloHost.Dispose();
    }

    // -------------------------------------------------------------------------
    // Test 1 — subscribe, publish, receive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Subscribe_ThenPublish_ItemIsReceivedByClient()
    {
        // Arrange: get the client-side stream provider and subscribe.
        IClusterClient clusterClient = _clientHost.Services.GetRequiredService<IClusterClient>();
        IStreamProvider clientProvider = clusterClient.GetStreamProvider("test");

        var streamId = StreamId.Create("chat", "room1");
        IAsyncStream<string?> clientStream = clientProvider.GetStream<string?>(streamId);

        var received = new List<string?>();
        var firstTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await clientStream.SubscribeAsync((msg, _) =>
        {
            lock (received)
            {
                received.Add(msg);
                if (received.Count == 1) firstTcs.TrySetResult(true);
                if (received.Count >= 2) secondTcs.TrySetResult(true);
            }
            return ValueTask.CompletedTask;
        });

        // Act: publish two items from the silo side in-process.
        IStreamProvider siloProvider =
            _siloHost.Services.GetRequiredKeyedService<IStreamProvider>("test");
        IAsyncStream<string?> siloStream = siloProvider.GetStream<string?>(streamId);

        await siloStream.OnNextAsync("hello");
        // Wait for first item to arrive before publishing the second.
        await firstTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await siloStream.OnNextAsync("world");

        // Assert: client receives both items within 3 s total.
        await secondTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        string?[] snapshot;
        lock (received) snapshot = received.ToArray();
        Assert.Equal(new string?[] { "hello", "world" }, snapshot);
    }

    // -------------------------------------------------------------------------
    // Test 2 — unsubscribe stops delivery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Unsubscribe_StopsDelivery()
    {
        // Arrange: subscribe.
        IClusterClient clusterClient = _clientHost.Services.GetRequiredService<IClusterClient>();
        IStreamProvider clientProvider = clusterClient.GetStreamProvider("test");

        var streamId = StreamId.Create("chat", "room-unsub");
        IAsyncStream<string?> clientStream = clientProvider.GetStream<string?>(streamId);

        var received = new List<string?>();
        var firstItemTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        StreamSubscriptionHandle<string?> handle = await clientStream.SubscribeAsync((msg, _) =>
        {
            received.Add(msg);
            firstItemTcs.TrySetResult(true);
            return ValueTask.CompletedTask;
        });

        IStreamProvider siloProvider =
            _siloHost.Services.GetRequiredKeyedService<IStreamProvider>("test");
        IAsyncStream<string?> siloStream = siloProvider.GetStream<string?>(streamId);

        // Publish once, wait for delivery.
        await siloStream.OnNextAsync("before");
        await firstItemTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Act: unsubscribe, then publish again.
        await handle.UnsubscribeAsync();
        await siloStream.OnNextAsync("after");

        // Give enough time for the second item to arrive if unsubscribe had no effect.
        await Task.Delay(200);

        // Assert: only the first item was received.
        Assert.Equal(["before"], received);
    }
}
