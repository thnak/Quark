using Microsoft.Extensions.Logging;
using Quark.Networking.Abstractions;

namespace Quark.Client;

/// <summary>
/// Default implementation of IClusterClient with smart routing and retry logic.
/// </summary>
public sealed class ClusterClient : IClusterClient
{
    private readonly IQuarkClusterMembership _clusterMembership;
    private readonly IQuarkTransport _transport;
    private readonly ClusterClientOptions _options;
    private readonly ILogger<ClusterClient> _logger;
    private readonly string _clientId;
    private bool _isConnected;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterClient"/> class.
    /// </summary>
    public ClusterClient(
        IQuarkClusterMembership clusterMembership,
        IQuarkTransport transport,
        ClusterClientOptions options,
        ILogger<ClusterClient> logger)
    {
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clientId = options.ClientId ?? Guid.NewGuid().ToString("N");
    }

    /// <inheritdoc />
    public IQuarkClusterMembership ClusterMembership => _clusterMembership;

    /// <inheritdoc />
    public IQuarkTransport Transport => _transport;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting client {ClientId} to cluster", _clientId);

        try
        {
            // Start cluster membership monitoring to discover silos
            await _clusterMembership.StartAsync(cancellationToken);
            _logger.LogInformation("Cluster membership monitoring started for client {ClientId}", _clientId);

            // Wait for at least one silo to be available
            var retries = 0;
            while (retries < _options.MaxRetries)
            {
                var silos = await _clusterMembership.GetActiveSilosAsync(cancellationToken);
                if (silos.Count > 0)
                {
                    _logger.LogInformation("Client {ClientId} discovered {Count} active silos", _clientId, silos.Count);
                    break;
                }

                retries++;
                if (retries < _options.MaxRetries)
                {
                    _logger.LogWarning(
                        "No active silos found for client {ClientId}, retrying... ({Retry}/{MaxRetries})",
                        _clientId,
                        retries,
                        _options.MaxRetries);
                    await Task.Delay(_options.RetryDelay, cancellationToken);
                }
            }

            _isConnected = true;
            _logger.LogInformation("Client {ClientId} connected to cluster successfully", _clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect client {ClientId} to cluster", _clientId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting client {ClientId} from cluster", _clientId);

        try
        {
            _isConnected = false;

            // Stop cluster membership monitoring
            await _clusterMembership.StopAsync(cancellationToken);
            _logger.LogInformation("Cluster membership monitoring stopped for client {ClientId}", _clientId);

            _logger.LogInformation("Client {ClientId} disconnected from cluster successfully", _clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while disconnecting client {ClientId} from cluster", _clientId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<QuarkEnvelope> SendAsync(QuarkEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Client is not connected. Call ConnectAsync first.");
        }

        if (envelope == null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        // Use consistent hashing to determine target silo
        var targetSiloId = _clusterMembership.GetActorSilo(envelope.ActorId, envelope.ActorType);
        if (targetSiloId == null)
        {
            throw new InvalidOperationException("No silos available to handle the request.");
        }

        _logger.LogDebug(
            "Routing envelope for actor {ActorId} ({ActorType}) to silo {SiloId}",
            envelope.ActorId,
            envelope.ActorType,
            targetSiloId);

        // Send with retry logic
        var retries = 0;
        Exception? lastException = null;

        while (retries <= _options.MaxRetries)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.RequestTimeout);

                var response = await _transport.SendAsync(targetSiloId, envelope, timeoutCts.Token);
                _logger.LogDebug("Received response for actor {ActorId}", envelope.ActorId);
                return response;
            }
            catch (Exception ex) when (retries < _options.MaxRetries)
            {
                lastException = ex;
                retries++;
                _logger.LogWarning(
                    ex,
                    "Request failed for actor {ActorId}, retrying... ({Retry}/{MaxRetries})",
                    envelope.ActorId,
                    retries,
                    _options.MaxRetries);
                await Task.Delay(_options.RetryDelay, cancellationToken);

                // Re-resolve the target silo in case cluster topology changed
                targetSiloId = _clusterMembership.GetActorSilo(envelope.ActorId, envelope.ActorType);
                if (targetSiloId == null)
                {
                    throw new InvalidOperationException("No silos available to handle the request.");
                }
            }
        }

        // All retries exhausted
        throw new InvalidOperationException(
            $"Failed to send request to actor {envelope.ActorId} after {_options.MaxRetries} retries.",
            lastException);
    }

    /// <inheritdoc />
    public TProxy GetActorProxy<TProxy>(string actorId) where TProxy : class
    {
        if (string.IsNullOrEmpty(actorId))
        {
            throw new ArgumentException("Actor ID cannot be null or empty.", nameof(actorId));
        }

        if (!_isConnected)
        {
            throw new InvalidOperationException("Client is not connected. Call ConnectAsync first.");
        }

        // Use reflection to find and instantiate the proxy type
        // The proxy type should be named {ActorName}Proxy and have a constructor taking (IClusterClient, string)
        var proxyTypeName = typeof(TProxy).Name.Replace("I", "") + "Proxy";
        var proxyNamespace = typeof(TProxy).Namespace + ".Generated";
        
        // Try to find the proxy type in the same assembly as TProxy
        var proxyType = typeof(TProxy).Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == proxyTypeName && t.Namespace == proxyNamespace);

        if (proxyType == null)
        {
            // Try without the "I" prefix replacement
            proxyTypeName = typeof(TProxy).Name + "Proxy";
            proxyType = typeof(TProxy).Assembly.GetTypes()
                .FirstOrDefault(t => t.Name == proxyTypeName && t.Namespace == proxyNamespace);
        }

        if (proxyType == null)
        {
            throw new InvalidOperationException(
                $"Could not find proxy type for {typeof(TProxy).Name}. " +
                "Ensure the ProtoSourceGenerator has run and generated the proxy.");
        }

        // Create an instance of the proxy
        var proxy = Activator.CreateInstance(proxyType, this, actorId);
        if (proxy == null)
        {
            throw new InvalidOperationException($"Failed to create instance of {proxyType.Name}.");
        }

        return (TProxy)proxy;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _transport.Dispose();
    }
}
