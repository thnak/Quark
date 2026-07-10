using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;
using Quark.Runtime.Clustering;

namespace Quark.Runtime;

/// <summary>
///     <see cref="IHostedService" /> that drives the Quark silo lifecycle.
///     On <see cref="StartAsync" /> it applies all deferred grain-type and transport-dispatcher
///     registrations so the runtime is fully wired before serving calls.
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

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting Quark silo '{SiloName}' [{ClusterId}/{ServiceId}] at {SiloAddress}",
            _options.SiloName,
            _options.ClusterId,
            _options.ServiceId,
            _options.SiloAddress);

        // Apply deferred grain-type registrations (AddGrainBehavior<TInterface, TBehavior> calls).
        ApplyGrainRegistrations();
        // Apply deferred compile-time behavior factories (AddGrainBehavior<,>(factory: ...) calls).
        ApplyBehaviorFactoryRegistrations();
        // Apply deferred placement-strategy registrations (AddGrainPlacementStrategy<> calls).
        ApplyPlacementStrategyRegistrations();
        // Apply deferred user-service-provider-factory registrations (AddGrainUserServiceProviderFactory calls).
        ApplyUserServiceProviderFactoryRegistrations();
        // Apply deferred transport-dispatcher registrations (AddGrainTransportDispatcher() calls).
        ApplyTransportDispatcherRegistrations();

        if (_services.GetService<SiloMessagePump>() is { } messagePump)
        {
            await messagePump.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        await _lifecycle.StartAsync(cancellationToken).ConfigureAwait(false);

        // Register this silo in the cluster router so remote silos can forward calls to us.
        if (_services.GetService<ISiloRouter>() is { } router)
        {
            IGrainCallInvoker invoker = _services.GetRequiredService<IGrainCallInvoker>();
            router.Register(_options.SiloAddress, invoker);
            _logger.LogDebug("Silo {SiloAddress} registered in cluster router.", _options.SiloAddress);
        }

        _logger.LogInformation("Quark silo '{SiloName}' is active.", _options.SiloName);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Quark silo '{SiloName}'...", _options.SiloName);

        // Remove from router before stopping so no new calls are routed here.
        var router = _services.GetService<ISiloRouter>();
        router?.Unregister(_options.SiloAddress);

        if (_services.GetService<SiloMessagePump>() is { } messagePump)
        {
            await messagePump.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        await _lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);

        // Drain all grain activations while the DI root is still live.
        // GrainActivationTable is disposed again by the container after StopAsync returns, but
        // by then _activations is empty so that second call is a no-op.
        if (_services.GetService<GrainActivationTable>() is { } table)
        {
            await table.DisposeAsync().ConfigureAwait(false);
        }

        if (_services.GetService<QuarkOnlyServiceProviderHolder>()?.Provider is IAsyncDisposable quarkOnlyProvider)
        {
            await quarkOnlyProvider.DisposeAsync().ConfigureAwait(false);
        }

        _logger.LogInformation("Quark silo '{SiloName}' stopped.", _options.SiloName);
    }

    // -----------------------------------------------------------------------

    private void ApplyGrainRegistrations()
    {
        if (_services.GetService<GrainTypeRegistry>() is not { } typeRegistry)
        {
            return;
        }

        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration reg in _services.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration>())
        {
            reg.Apply(typeRegistry);
        }
    }

    private void ApplyBehaviorFactoryRegistrations()
    {
        if (_services.GetService<GrainBehaviorFactoryRegistry>() is not { } factoryRegistry)
        {
            return;
        }

        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration reg in
                 _services.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration>())
        {
            reg.Apply(factoryRegistry);
        }
    }

    private void ApplyPlacementStrategyRegistrations()
    {
        if (_services.GetService<AttributePlacementStrategyResolver>() is not { } placementRegistry)
        {
            return;
        }

        foreach (RuntimeServiceCollectionExtensions.IGrainPlacementStrategyRegistration reg in
                 _services.GetServices<RuntimeServiceCollectionExtensions.IGrainPlacementStrategyRegistration>())
        {
            reg.Apply(placementRegistry);
        }
    }

    private void ApplyTransportDispatcherRegistrations()
    {
        if (_services.GetService<TransportGrainDispatcherRegistry>() is not { } dispatcherRegistry)
        {
            return;
        }

        foreach (RuntimeServiceCollectionExtensions.IGrainTransportDispatcherRegistration reg in _services.GetServices<RuntimeServiceCollectionExtensions.IGrainTransportDispatcherRegistration>())
        {
            reg.Apply(dispatcherRegistry);
        }
    }

    private void ApplyUserServiceProviderFactoryRegistrations()
    {
        if (_services.GetService<IUserServiceProviderRegistry>() is not { } registry)
        {
            return;
        }

        var factoryRegistrations = _services
            .GetServices<RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration>()
            .ToList();

        foreach (RuntimeServiceCollectionExtensions.IUserServiceProviderFactoryRegistration reg in factoryRegistrations)
        {
            reg.Apply(registry, _services);
        }

        if (factoryRegistrations.Count == 0)
        {
            return;
        }

        GrainTypeRegistry mainTypeRegistry = _services.GetRequiredService<GrainTypeRegistry>();
        GrainBehaviorFactoryRegistry mainFactoryRegistry = _services.GetRequiredService<GrainBehaviorFactoryRegistry>();

        var quarkOnly = new ServiceCollection();
        quarkOnly.AddSingleton(mainTypeRegistry);
        quarkOnly.AddSingleton<IGrainTypeRegistry>(mainTypeRegistry);
        quarkOnly.AddSingleton(mainFactoryRegistry);
        quarkOnly.AddScoped<ActivationShellAccessor>();
        quarkOnly.AddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
        quarkOnly.AddScoped<CallContext>();
        quarkOnly.AddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
        quarkOnly.AddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
        quarkOnly.AddScoped<IBehaviorResolver, BehaviorResolver>();

        foreach (RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration marker in
                 _services.GetServices<RuntimeServiceCollectionExtensions.IQuarkOwnedServiceRegistration>())
        {
            marker.Apply(quarkOnly);
        }

        _services.GetRequiredService<QuarkOnlyServiceProviderHolder>().Provider = quarkOnly.BuildServiceProvider();
    }
}
