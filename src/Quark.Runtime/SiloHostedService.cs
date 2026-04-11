using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quark.Runtime;

/// <summary>
/// <see cref="IHostedService"/> that drives the Quark silo lifecycle.
/// Subscribes to <see cref="IHostApplicationLifetime"/> to initiate graceful shutdown.
/// </summary>
public sealed class SiloHostedService : IHostedService
{
    private readonly LifecycleSubject _lifecycle;
    private readonly ILogger<SiloHostedService> _logger;
    private readonly SiloRuntimeOptions _options;

    /// <summary>Initialises the hosted service.</summary>
    public SiloHostedService(
        LifecycleSubject lifecycle,
        IOptions<SiloRuntimeOptions> options,
        ILogger<SiloHostedService> logger)
    {
        _lifecycle = lifecycle;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting Quark silo '{SiloName}' [{ClusterId}/{ServiceId}] at {SiloAddress}",
            _options.SiloName,
            _options.ClusterId,
            _options.ServiceId,
            _options.SiloAddress);

        await _lifecycle.StartAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Quark silo '{SiloName}' is active.", _options.SiloName);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Quark silo '{SiloName}'...", _options.SiloName);

        await _lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Quark silo '{SiloName}' stopped.", _options.SiloName);
    }
}
