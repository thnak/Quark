using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;

namespace Quark.Runtime.Clustering;

/// <summary>
///     Background service that keeps this silo's membership entry alive and detects dead silos.
///     Writes IAmAlive heartbeats every <see cref="IAmAliveInterval" /> and removes silos whose
///     heartbeat has been absent longer than <see cref="DeadSiloThreshold" /> from the router.
/// </summary>
public sealed class MembershipOracle : BackgroundService
{
    internal static readonly TimeSpan IAmAliveInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan DeadSiloThreshold = TimeSpan.FromSeconds(30);

    private readonly IGrainDirectory _directory;
    private readonly ILogger<MembershipOracle> _logger;
    private readonly SiloRuntimeOptions _options;
    private readonly ISiloRouter _router;
    private readonly IMembershipTable _table;

    /// <summary>Initialises the oracle.</summary>
    public MembershipOracle(
        IMembershipTable table,
        ISiloRouter router,
        IGrainDirectory directory,
        IOptions<SiloRuntimeOptions> options,
        ILogger<MembershipOracle> logger)
    {
        _table = table;
        _router = router;
        _directory = directory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _table.InsertRowAsync(new MembershipEntry
        {
            SiloAddress = _options.SiloAddress,
            SiloName = _options.SiloName,
            Status = SiloStatus.Active,
            IAmAlive = DateTime.UtcNow,
        }, stoppingToken).ConfigureAwait(false);

        _logger.LogDebug("MembershipOracle: silo {SiloAddress} marked Active.", _options.SiloAddress);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(IAmAliveInterval, stoppingToken).ConfigureAwait(false);
                await _table.UpdateIAmAliveAsync(_options.SiloAddress, DateTime.UtcNow, stoppingToken)
                    .ConfigureAwait(false);
                await EvictDeadSilosAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }

        await MarkSelfDeadAsync().ConfigureAwait(false);
    }

    private async Task EvictDeadSilosAsync(CancellationToken ct)
    {
        IReadOnlyList<MembershipEntry> all = await _table.ReadAllAsync(ct).ConfigureAwait(false);
        foreach (MembershipEntry entry in all)
        {
            if (entry.SiloAddress == _options.SiloAddress) continue;
            if (entry.Status == SiloStatus.Dead) continue;
            if (DateTime.UtcNow - entry.IAmAlive <= DeadSiloThreshold) continue;

            _logger.LogWarning(
                "MembershipOracle: silo {SiloAddress} has not sent IAmAlive for {Elapsed:g}; marking Dead.",
                entry.SiloAddress,
                DateTime.UtcNow - entry.IAmAlive);

            entry.Status = SiloStatus.Dead;
            await _table.UpdateRowAsync(entry, ct).ConfigureAwait(false);
            _router.Unregister(entry.SiloAddress);
        }
    }

    private async Task MarkSelfDeadAsync()
    {
        try
        {
            await _table.UpdateRowAsync(new MembershipEntry
            {
                SiloAddress = _options.SiloAddress,
                SiloName = _options.SiloName,
                Status = SiloStatus.Dead,
                IAmAlive = DateTime.UtcNow,
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MembershipOracle: failed to mark self as Dead on shutdown.");
        }
    }
}
