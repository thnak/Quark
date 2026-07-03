using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Runtime.Clustering;

namespace Quark.Testing.Harness;

/// <summary>
///     Represents a single silo running in-process for testing purposes.
/// </summary>
public sealed class TestSilo : IAsyncDisposable
{
    private IHost? _host;
    private readonly SharedTestClusterState? _sharedState;

    internal TestSilo(string name, int siloPort, int gatewayPort, TestClusterOptions options,
        SharedTestClusterState? sharedState = null)
    {
        Name = name;
        SiloPort = siloPort;
        GatewayPort = gatewayPort;
        Options = options;
        _sharedState = sharedState;
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Resolves a service from this silo's DI container.</summary>
    public T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    /// <summary>Starts the silo.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // If clustering is enabled, inject shared state before caller services so they can override.
        if (_sharedState is not null)
        {
            // Override default single-node directory with the shared one.
            builder.Services.RemoveAll<IGrainDirectory>();
            builder.Services.AddSingleton<IGrainDirectory>(_sharedState.Directory);
            builder.Services.AddSingleton<InMemoryGrainDirectory>(_sharedState.Directory);

            // Shared router and membership table.
            builder.Services.AddSingleton<ISiloRouter>(_sharedState.Router);
            builder.Services.AddSingleton<IMembershipTable>(_sharedState.MembershipTable);

            // Per-silo address (unique port per silo).
            builder.Services.Configure<SiloRuntimeOptions>(o =>
            {
                o.SiloName = Name;
                o.SiloAddress = SiloAddress.Loopback(SiloPort);
                o.GatewayAddress = SiloAddress.Loopback(GatewayPort);
            });

            builder.Services.AddHostedService<MembershipOracle>();
        }

        if (Options.TimeProvider is not null)
        {
            builder.Services.AddSingleton(Options.TimeProvider);
        }

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
}
