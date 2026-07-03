namespace Quark.Tests.Unit.FailureSemantics;

/// <summary>
///     DI singleton test double controlling whether <see cref="FlakyActivationBehavior" />'s
///     constructor throws, and counting how many separate construction attempts were made.
///     A behavior's own fields can't observe this across activation attempts (a fresh instance
///     is constructed each time, and the faulted attempt's instance is discarded entirely), so
///     the counter has to live outside the grain.
/// </summary>
public sealed class ActivationGate
{
    public bool ShouldFail { get; set; }

    public int AttemptCount { get; private set; }

    public void RecordAttempt() => AttemptCount++;
}
