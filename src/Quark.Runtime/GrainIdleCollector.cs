using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;

namespace Quark.Runtime;

/// <summary>
///     Background service that periodically deactivates grains that have been idle
///     longer than <see cref="SiloRuntimeOptions.GrainCollectionAge"/>.
///     Disabled when <c>GrainCollectionAge == TimeSpan.Zero</c> (the default).
/// </summary>
internal sealed class GrainIdleCollector : BackgroundService
{
    private readonly GrainActivationTable _activationTable;
    private readonly SiloRuntimeOptions _options;

    public GrainIdleCollector(
        GrainActivationTable activationTable,
        IOptions<SiloRuntimeOptions> options)
    {
        _activationTable = activationTable;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.GrainCollectionAge == TimeSpan.Zero)
            return;

        using var timer = new PeriodicTimer(_options.GrainCollectionInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            CollectIdleGrains();
        }
    }

    private void CollectIdleGrains()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var (_, activation) in _activationTable.GetActiveActivations())
        {
            if (activation.IsIdleLongerThan(_options.GrainCollectionAge, now)
                && activation.Context.IsDeactivationAllowed(now))
            {
                activation.Context.Deactivate(DeactivationReason.IdleTimeout);
            }
        }
    }
}
