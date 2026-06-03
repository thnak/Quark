namespace Quark.Core.Abstractions.Timers;

/// <summary>
///     Handle for a grain-scoped timer created via <c>RegisterGrainTimer</c>.
///     Dispose to cancel. Drop-in equivalent of Orleans' <c>IGrainTimer</c>.
/// </summary>
public interface IGrainTimer : IDisposable
{
    /// <summary>Changes the timer's due time and period.</summary>
    void Change(TimeSpan dueTime, TimeSpan period);
}
