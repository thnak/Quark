using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;
using Quark.Transport.Abstractions;

namespace Quark.Runtime.Clustering;

/// <summary>
///     Watches cluster membership and installs/removes a <see cref="SiloCallInvoker" /> per
///     Active peer into the <see cref="ISiloRouter" />, owning one pooled
///     <see cref="SiloPeerConnection" /> per peer.
///     Connection lifecycle: dial-on-demand (lazy), pool, close-on-death.
///     Reconnect policy (backoff, retry replay) is deferred to #60.
/// </summary>
public sealed class PeerConnectionManager : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly IMembershipTable _table;
    private readonly ISiloRouter _router;
    private readonly DefaultClusterMembershipSnapshot _snapshot;
    private readonly SiloAddress _self;
    private readonly ITransport _transport;
    private readonly ILogger<PeerConnectionManager> _logger;
    private readonly GrainMessageSerializer _grainSerializer;
    private readonly MessageSerializer _messageSerializer;

    // Mutable state guarded by the single background-loop thread only
    // (no concurrent mutation after StartAsync completes).
    private readonly Dictionary<SiloAddress, SiloPeerConnection> _connections = new();
    private bool _warnedNonDeterministic;

    public PeerConnectionManager(
        IMembershipTable table,
        ISiloRouter router,
        DefaultClusterMembershipSnapshot snapshot,
        IOptions<SiloRuntimeOptions> options,
        ITransport transport,
        ILogger<PeerConnectionManager> logger,
        GrainMessageSerializer? grainSerializer = null,
        MessageSerializer? messageSerializer = null)
    {
        _table = table;
        _router = router;
        _snapshot = snapshot;
        _self = options.Value.SiloAddress;
        _transport = transport;
        _logger = logger;
        _grainSerializer = grainSerializer!;
        _messageSerializer = messageSerializer!;
    }

    /// <summary>
    ///     Performs the initial membership sync synchronously before returning, so peers are
    ///     registered by the time StartAsync completes rather than racing the background loop.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, stoppingToken).ConfigureAwait(false);
                await RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        // Close all peer connections on shutdown
        foreach (SiloPeerConnection conn in _connections.Values)
        {
            try { await conn.DisposeAsync().ConfigureAwait(false); } catch { }
        }
        _connections.Clear();
    }

    /// <summary>
    ///     Reads the membership table and syncs router + snapshot. Internal for test access.
    /// </summary>
    internal async Task RefreshAsync(CancellationToken ct)
    {
        IReadOnlyList<MembershipEntry> all = await _table.ReadAllAsync(ct).ConfigureAwait(false);

        var activeSilos = new List<SiloAddress>();
        var activePeers = new HashSet<SiloAddress>();

        foreach (MembershipEntry entry in all)
        {
            if (entry.Status != SiloStatus.Active) continue;
            activeSilos.Add(entry.SiloAddress);
            if (entry.SiloAddress != _self)
                activePeers.Add(entry.SiloAddress);
        }

        // Register new Active peers
        foreach (SiloAddress peer in activePeers)
        {
            if (_connections.ContainsKey(peer)) continue;

            var conn = new SiloPeerConnection(_transport, _messageSerializer, peer);
            _connections[peer] = conn;
            var invoker = new SiloCallInvoker(peer, conn, _grainSerializer, _messageSerializer);
            _router.Register(peer, invoker);
            _logger.LogDebug("PeerConnectionManager: registered peer {Peer}.", peer);
        }

        // Unregister Dead / gone peers
        foreach (SiloAddress peer in _connections.Keys.ToArray())
        {
            if (activePeers.Contains(peer)) continue;

            _router.Unregister(peer);
            SiloPeerConnection conn = _connections[peer];
            _connections.Remove(peer);
            try { await conn.DisposeAsync().ConfigureAwait(false); } catch { }
            _logger.LogDebug("PeerConnectionManager: unregistered dead peer {Peer}.", peer);
        }

        // Update the membership snapshot used by LocalGrainCallInvoker placement
        _snapshot.Update(activeSilos);

        // One-time warning: non-deterministic strategies risk duplicate activation cross-process
        if (!_warnedNonDeterministic && activeSilos.Count > 1)
        {
            _warnedNonDeterministic = true;
            _logger.LogWarning(
                "Silo-to-silo transport is active with {Count} silos. " +
                "Single-activation across processes is guaranteed only for [HashBasedPlacement]. " +
                "Random/PreferLocal/StatelessWorker placement may produce duplicate activations " +
                "until a distributed grain directory is implemented (follow-up to #126).",
                activeSilos.Count);
        }
    }
}
