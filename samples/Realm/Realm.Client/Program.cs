using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client =>
    {
        client.UseLocalhostGateway(30010);
        client.Services.AddRealmGrainInterfacesGrainProxies();
    })
    .Build();

await host.StartAsync();
Console.WriteLine("Realm client connected. (Phase 0: no commands yet — behaviors come in Phase 1+)");
await host.StopAsync();
