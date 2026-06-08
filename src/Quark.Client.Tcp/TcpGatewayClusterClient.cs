using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Streaming.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     <see cref="IClusterClient" /> implementation that connects to a silo's gateway over TCP.
/// </summary>
public sealed class TcpGatewayClusterClient : IClusterClient
{
    private readonly TcpGatewayConnection _connection;
    private readonly TcpGatewayGrainFactory _factory;
    private readonly TcpGatewayClientOptions _options;
    private readonly IServiceProvider _services;

    public TcpGatewayClusterClient(
        TcpGatewayConnection connection,
        TcpGatewayGrainFactory factory,
        IOptions<TcpGatewayClientOptions> options,
        IServiceProvider services)
    {
        _connection = connection;
        _factory = factory;
        _options = options.Value;
        _services = services;
    }

    public bool IsInitialized { get; private set; }

    public async Task Connect(Func<Exception, Task>? retryFilter = null)
    {
        await _connection.ConnectAsync(_options.GatewayEndpoint).ConfigureAwait(false);
        IsInitialized = true;
    }

    public async Task Close()
    {
        await _connection.CloseAsync().ConfigureAwait(false);
        IsInitialized = false;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        IsInitialized = false;
    }

    public TGrainInterface GetGrain<TGrainInterface>(string key)
        where TGrainInterface : IGrainWithStringKey
        => _factory.GetGrain<TGrainInterface>(key);

    public TGrainInterface GetGrain<TGrainInterface>(long key)
        where TGrainInterface : IGrainWithIntegerKey
        => _factory.GetGrain<TGrainInterface>(key);

    public TGrainInterface GetGrain<TGrainInterface>(Guid key)
        where TGrainInterface : IGrainWithGuidKey
        => _factory.GetGrain<TGrainInterface>(key);

    public TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension)
        where TGrainInterface : IGrainWithIntegerCompoundKey
        => _factory.GetGrain<TGrainInterface>(key, keyExtension);

    public TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension)
        where TGrainInterface : IGrainWithGuidCompoundKey
        => _factory.GetGrain<TGrainInterface>(key, keyExtension);

    public IGrain GetGrain(Type grainInterfaceType, string key)
        => _factory.GetGrain(grainInterfaceType, key);

    public IGrain GetGrain(Type grainInterfaceType, Guid key)
        => _factory.GetGrain(grainInterfaceType, key);

    public IGrain GetGrain(Type grainInterfaceType, long key)
        => _factory.GetGrain(grainInterfaceType, key);

    public IStreamProvider GetStreamProvider(string name)
        => _services.GetRequiredKeyedService<IStreamProvider>(name);
}
