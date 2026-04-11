using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quark.Runtime;

/// <summary>
/// <see cref="IHostedService"/> that drives the Quark silo lifecycle.
/// On <see cref="StartAsync"/> it also applies all deferred grain-type and
/// method-invoker registrations so the runtime is fully wired before serving calls.
/// </summary>
public sealed class SiloHostedService : IHostedService
{
    private readonly LifecycleSubject _lifecycle;
    private readonly ILogger<SiloHostedService> _logger;
    private readonly SiloRuntimeOptions _options;
    private readonly IServiceProvider _services;

    /// <summary>Initialises the hosted service.</summary>
    public SiloHostedService(
        LifecycleSubject lifecycle,
        IOptions<SiloRuntimeOptions> options,
        ILogger<SiloHostedService> logger,
        IServiceProvider services)
    {
        _lifecycle = lifecycle;
        _options = options.Value;
        _logger = logger;
        _services = services;
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

        // Apply deferred grain-type registrations (AddGrain<T> calls).
        ApplyGrainRegistrations();
        // Apply deferred method-invoker registrations (AddGrainMethodInvoker<TGrain,TInvoker> calls).
        ApplyMethodInvokerRegistrations();

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

    // -----------------------------------------------------------------------

    private void ApplyGrainRegistrations()
    {
        var typeRegistry = _services.GetService(typeof(GrainTypeRegistry)) as GrainTypeRegistry;
        if (typeRegistry is null) return;

        var registrations = (IEnumerable<object>?)_services.GetService(
            typeof(IEnumerable<RuntimeServiceCollectionExtensions.IGrainRegistration>)) ?? [];

        foreach (var reg in registrations.Cast<RuntimeServiceCollectionExtensions.IGrainRegistration>())
        {
            reg.Apply(typeRegistry);
        }
    }

    private void ApplyMethodInvokerRegistrations()
    {
        var invokerRegistry = _services.GetService(typeof(GrainMethodInvokerRegistry)) as GrainMethodInvokerRegistry;
        if (invokerRegistry is null) return;

        var registrations = (IEnumerable<object>?)_services.GetService(
            typeof(IEnumerable<RuntimeServiceCollectionExtensions.IGrainMethodInvokerRegistration>)) ?? [];

        foreach (var reg in registrations.Cast<RuntimeServiceCollectionExtensions.IGrainMethodInvokerRegistration>())
        {
            reg.Apply(invokerRegistry, _services);
        }
    }
}
