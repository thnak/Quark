using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Abstractions.Migration;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Background service that registers actor versions provided by the application on startup.
/// The term "Manual" refers to the application explicitly passing the generated registry,
/// as opposed to automatic runtime discovery which would require reflection.
/// Part of Phase 10.1.1 (Zero Downtime & Rolling Upgrades - Automatic Version Detection).
/// </summary>
internal sealed class VersionManualRegistrationService : IHostedService
{
    private readonly IVersionTracker _versionTracker;
    private readonly IReadOnlyDictionary<string, AssemblyVersionInfo> _versionMap;
    private readonly ILogger<VersionManualRegistrationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionManualRegistrationService"/> class.
    /// </summary>
    public VersionManualRegistrationService(
        IVersionTracker versionTracker,
        IReadOnlyDictionary<string, AssemblyVersionInfo> versionMap,
        ILogger<VersionManualRegistrationService> logger)
    {
        _versionTracker = versionTracker ?? throw new ArgumentNullException(nameof(versionTracker));
        _versionMap = versionMap ?? throw new ArgumentNullException(nameof(versionMap));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the service and registers the provided versions.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _versionTracker.RegisterSiloVersionsAsync(_versionMap, cancellationToken);
            
            _logger.LogInformation(
                "Version registration completed: {Count} actor types registered from generated registry",
                _versionMap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to register versions from generated registry");
            throw;
        }
    }

    /// <summary>
    /// Stops the service (no-op for this service).
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
