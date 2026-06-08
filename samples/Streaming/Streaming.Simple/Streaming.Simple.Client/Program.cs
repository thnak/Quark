using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Hosting;
using Quark.Streaming.Abstractions;
using Streaming.Common;
using Streaming.Simple.GrainInterfaces;

var key = Guid.NewGuid();

using var host = Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client =>
    {
        client.UseLocalhostGateway(30003);
        client.AddTcpClientStreams(Constants.StreamProvider);
        client.Services.AddGrainProxy<IProducerGrain, ProducerGrainProxy>();
        client.Services.AddGrainProxy<IConsumerGrain, ConsumerGrainProxy>();
    })
    .Build();

await host.StartAsync();

var clusterClient = host.Services.GetRequiredService<IClusterClient>();
var streamProvider = clusterClient.GetStreamProvider(Constants.StreamProvider);

var producer = clusterClient.GetGrain<IProducerGrain>("producer-1");
var consumer = clusterClient.GetGrain<IConsumerGrain>(key);

// 1. Start producer timer — publishes an int every second
await producer.StartProducing(Constants.StreamNamespace, key);
Console.WriteLine("Producer started.");

// 2. Consumer grain subscribes explicitly
await consumer.Subscribe(StreamId.Create(Constants.StreamNamespace, key));
Console.WriteLine("Consumer grain subscribed.");

// 3. Client subscribes directly over TCP push
var stream = streamProvider.GetStream<int>(StreamId.Create(Constants.StreamNamespace, key));
var handle = await stream.SubscribeAsync((item, _) =>
{
    Console.WriteLine($"[Client] Received: {item}");
    return Task.CompletedTask;
});
Console.WriteLine("Client subscribed. Press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) { }

await producer.StopProducing();
await consumer.Unsubscribe();
await handle.UnsubscribeAsync();

await host.StopAsync();
