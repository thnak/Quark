using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Quark.Testing.Harness;

/// <summary>
/// Represents a single silo running in-process for testing purposes.
/// </summary>
public sealed class TestSilo : IAsyncDisposable
{
    private IHost? _host;

    internal TestSilo(string name, int siloPort, int gatewayPort, TestClusterOptions options)
    {
        Name = name;
        SiloPort = siloPort;
        GatewayPort = gatewayPort;
        Options = options;
    }

    /// <summary>Friendly name of this silo (used in logs and diagnostics).</summary>
    public string Name { get; }

    /// <summary>Port used for silo-to-silo communication.</summary>
    public int SiloPort { get; }

    /// <summary>Port used for client gateway connections.</summary>
    public int GatewayPort { get; }

    internal TestClusterOptions Options { get; }

    /// <summary>Whether this silo has been started.</summary>
    public bool IsStarted => _host is not null;

    /// <summary>Service provider for this silo host.</summary>
    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("TestSilo is not started.");

    /// <summary>Resolves a service from this silo's DI container.</summary>
    public T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();

    /// <summary>Starts the silo.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);

        // Minimal silo setup — will expand in M3 to include real silo runtime.
        Options.ConfigureSiloServices?.Invoke(builder.Services);
        Options.ConfigureClientServices?.Invoke(builder.Services);

        _host = builder.Build();
        await _host.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the silo gracefully.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host is not null)
        {
            await _host.StopAsync(cancellationToken).ConfigureAwait(false);
            _host.Dispose();
            _host = null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
