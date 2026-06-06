using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;
using Quark.Core.Abstractions.Timers;

namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Abstract base class for all grain implementations.
/// </summary>
public abstract class Grain : IGrain
{
    private IGrainContext? _grainContext;

    /// <summary>Gets the context for this grain activation.</summary>
    protected IGrainContext GrainContext =>
        _grainContext ?? throw new InvalidOperationException("Grain has not been activated.");

    /// <summary>Gets the identity of this grain.</summary>
    protected GrainId GrainId => GrainContext.GrainId;

    /// <summary>
    ///     Gets the grain factory for this activation.
    ///     Use to obtain references to other grains from inside a grain.
    ///     Drop-in equivalent of Orleans' <c>GrainFactory</c> property.
    /// </summary>
    protected IGrainFactory GrainFactory => GrainContext.GrainFactory;

    /// <summary>Gets the DI service provider for this activation.</summary>
    protected IServiceProvider ServiceProvider => GrainContext.ServiceProvider;

    private const string NoReminderServiceMessage =
        "No IReminderService is registered. Call AddInMemoryReminders() or AddRedisReminders() when building the silo.";

    /// <summary>Called when the grain is first activated. Override to perform async initialization.</summary>
    public virtual Task OnActivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>Called before the grain is deactivated. Override to persist state or clean up.</summary>
    public virtual Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Requests that this grain be deactivated once it becomes idle.
    ///     Drop-in equivalent of Orleans' <c>DeactivateOnIdle()</c>.
    /// </summary>
    protected void DeactivateOnIdle()
    {
        GrainContext.Deactivate(DeactivationReason.ApplicationRequested);
    }

    /// <summary>
    ///     Delays automatic deactivation by <paramref name="timeSpan" /> from now.
    ///     Has no effect if the idle-timeout collector is disabled (<c>GrainCollectionAge = TimeSpan.Zero</c>).
    ///     Drop-in equivalent of Orleans' <c>DelayDeactivation(TimeSpan)</c>.
    /// </summary>
    protected void DelayDeactivation(TimeSpan timeSpan)
    {
        GrainContext.DelayDeactivation(timeSpan);
    }

    /// <summary>
    ///     Registers a timer that fires the given <paramref name="callback" /> on this grain's scheduler.
    ///     The timer is automatically cancelled when the grain deactivates.
    ///     Drop-in equivalent of Orleans' <c>RegisterGrainTimer</c>.
    /// </summary>
    protected IGrainTimer RegisterGrainTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
        => GrainContext.RegisterTimer(callback, state, options);

    /// <summary>
    ///     Stateless convenience overload — no state object is passed to the callback.
    ///     Drop-in equivalent of Orleans' <c>RegisterGrainTimer(callback, dueTime, period)</c>.
    /// </summary>
    protected IGrainTimer RegisterGrainTimer(
        Func<CancellationToken, Task> callback,
        TimeSpan dueTime,
        TimeSpan period)
        => RegisterGrainTimer<object?>(
            (_, ct) => callback(ct),
            null,
            new GrainTimerCreationOptions { DueTime = dueTime, Period = period });

    /// <summary>
    ///     Registers or updates a durable reminder for this grain.
    ///     Requires an <see cref="IReminderService" /> registered in DI (e.g. <c>AddInMemoryReminders()</c>).
    ///     Drop-in equivalent of Orleans' <c>this.RegisterOrUpdateReminder()</c>.
    /// </summary>
    protected Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        string reminderName, TimeSpan dueTime, TimeSpan period)
        => (GrainContext.ReminderService
            ?? throw new InvalidOperationException(NoReminderServiceMessage))
            .RegisterOrUpdateReminderAsync(GrainId, reminderName, dueTime, period);

    /// <summary>
    ///     Cancels a previously registered reminder.
    ///     Drop-in equivalent of Orleans' <c>this.UnregisterReminder()</c>.
    /// </summary>
    protected Task UnregisterReminderAsync(IGrainReminder reminder)
        => (GrainContext.ReminderService
            ?? throw new InvalidOperationException(NoReminderServiceMessage))
            .UnregisterReminderAsync(GrainId, reminder.ReminderName);

    /// <summary>
    ///     Returns all reminders registered by this grain.
    ///     Drop-in equivalent of Orleans' <c>this.GetReminders()</c>.
    /// </summary>
    protected Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync()
        => (GrainContext.ReminderService
            ?? throw new InvalidOperationException(NoReminderServiceMessage))
            .GetRemindersAsync(GrainId);

    /// <summary>
    ///     Returns a proxy for this grain typed as <typeparamref name="TGrainInterface" />.
    ///     Useful when the grain needs to pass a reference to itself to another grain or observer.
    ///     Drop-in equivalent of Orleans' <c>this.AsReference&lt;T&gt;()</c>.
    /// </summary>
    protected TGrainInterface AsReference<TGrainInterface>()
        where TGrainInterface : IGrain
        => GrainFactory.GetGrain<TGrainInterface>(GrainId);

    protected string GetPrimaryKeyString() => GrainId.Key;

    protected Guid GetPrimaryKey() => Guid.ParseExact(GrainId.Key, "N");

    protected long GetPrimaryKeyLong() => long.Parse(GrainId.Key, System.Globalization.CultureInfo.InvariantCulture);

    protected Guid GetPrimaryKey(out string keyExtension)
    {
        int plus = GrainId.Key.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            keyExtension = string.Empty;
            return Guid.ParseExact(GrainId.Key, "N");
        }
        keyExtension = GrainId.Key[(plus + 1)..];
        return Guid.ParseExact(GrainId.Key[..plus], "N");
    }

    protected long GetPrimaryKeyLong(out string keyExtension)
    {
        int plus = GrainId.Key.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            keyExtension = string.Empty;
            return long.Parse(GrainId.Key, System.Globalization.CultureInfo.InvariantCulture);
        }
        keyExtension = GrainId.Key[(plus + 1)..];
        return long.Parse(GrainId.Key[..plus], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Framework-only. Called by the runtime to bind the grain to its activation context.
    /// </summary>
    internal void SetContext(IGrainContext context)
    {
        _grainContext = context;
    }
}
