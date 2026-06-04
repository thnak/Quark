using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;

namespace Quark.Reminders.Abstractions;

/// <summary>
///     Polls <see cref="IReminderStorage" /> on a fixed interval and fires due reminders
///     via <see cref="IGrainCallInvoker" /> using the well-known
///     <see cref="ReminderMethodIds.ReceiveReminder" /> method ID.
///     Register with <c>AddInMemoryReminders()</c> or <c>AddRedisReminders()</c>.
/// </summary>
public sealed class DefaultReminderService : IReminderService, IHostedService, IAsyncDisposable
{
    private readonly IGrainCallInvoker _invoker;
    private readonly ILogger<DefaultReminderService> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly IReminderStorage _storage;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public DefaultReminderService(
        IReminderStorage storage,
        IGrainCallInvoker invoker,
        IOptions<ReminderOptions> options,
        ILogger<DefaultReminderService> logger)
    {
        _storage = storage;
        _invoker = invoker;
        _pollInterval = options.Value.PollInterval;
        _logger = logger;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // ---- IReminderService ----

    public async Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        GrainId grainId, string name, TimeSpan dueTime, TimeSpan period,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero, nameof(period));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var entry = new ReminderEntry
        {
            GrainId = grainId,
            ReminderName = name,
            StartAt = now,
            Period = period,
            NextFireAt = now + dueTime
        };
        await _storage.UpsertAsync(entry, ct).ConfigureAwait(false);
        return new GrainReminder(name);
    }

    public Task UnregisterReminderAsync(GrainId grainId, string name, CancellationToken ct = default)
        => _storage.DeleteAsync(grainId, name, ct);

    public async Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync(
        GrainId grainId, CancellationToken ct = default)
    {
        IReadOnlyList<ReminderEntry> entries =
            await _storage.ReadByGrainAsync(grainId, ct).ConfigureAwait(false);
        return entries.Select(static e => (IGrainReminder)new GrainReminder(e.ReminderName)).ToList();
    }

    // ---- IHostedService ----

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
            throw new InvalidOperationException("DefaultReminderService is already running.");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_loopTask is not null)
                await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    // ---- Polling loop ----

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await FireDueRemindersAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error firing due reminders");
            }
        }
    }

    private async Task FireDueRemindersAsync(CancellationToken ct)
    {
        IReadOnlyList<ReminderEntry> all = await _storage.ReadAllAsync(ct).ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (ReminderEntry entry in all)
        {
            if (entry.NextFireAt > now) continue;

            // Advance NextFireAt BEFORE invoking — at-least-once delivery on crash.
            ReminderEntry updated = entry with { NextFireAt = entry.NextFireAt + entry.Period };
            await _storage.UpsertAsync(updated, ct).ConfigureAwait(false);

            var status = new TickStatus(entry.StartAt, entry.Period, entry.NextFireAt);
            await _invoker.InvokeVoidAsync(
                entry.GrainId,
                ReminderMethodIds.ReceiveReminder,
                [entry.ReminderName, status],
                ct).ConfigureAwait(false);
        }
    }
}
