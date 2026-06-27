using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Core;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Codecs;
using Quark.Streaming.InMemory;
using Quark.Transport.Tcp;
using Streaming.Common;
using Streaming.Simple.GrainInterfaces;
using Streaming.Simple.Grains;

IHost host = Host.CreateDefaultBuilder(args)
    .UseQuark(silo =>
    {
        silo.Services.AddQuarkRuntime();
        silo.Services.AddTcpTransport();
        silo.UseLocalhostClustering(gatewayPort: 30003);
        silo.Services.AddMemoryStreams(Constants.StreamProvider);
        silo.Services.AddStreamableCodec<int, Int32Codec>();

        silo.Services.AddGrainBehavior<IProducerGrain, ProducerBehavior>();
        silo.Services.AddGrainBehavior<IConsumerGrain, ConsumerBehavior>();
        silo.Services.AddGrainTransportDispatcher(
            new GrainType("ProducerGrain"),
            new ProducerGrainProxy_TransportDispatcher());
        silo.Services.AddGrainTransportDispatcher(
            new GrainType("ConsumerGrain"),
            new ConsumerGrainProxy_TransportDispatcher());

        silo.Services.AddScoped<IActivationMemory<ProducerState>>(sp =>
            new ActivationMemoryAccessor<ProducerState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<ProducerState>()));
        silo.Services.AddScoped<IActivationMemory<ConsumerState>>(sp =>
            new ActivationMemoryAccessor<ConsumerState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<ConsumerState>()));
    })
    .UseQuarkClient(client =>
    {
        client.Services.AddLocalClusterClient();
        client.Services.AddGrainProxy<IProducerGrain, ProducerGrainProxy>();
        client.Services.AddGrainProxy<IConsumerGrain, ConsumerGrainProxy>();
    })
    .Build();

await host.StartAsync();
Console.WriteLine("Streaming.Simple silo ready on port 30003. Press Ctrl+C to stop.");
await host.WaitForShutdownAsync();
