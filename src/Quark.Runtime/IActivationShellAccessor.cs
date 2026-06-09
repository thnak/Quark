namespace Quark.Runtime;

/// <summary>
///     Scoped bridge between the per-call <see cref="IServiceScope" /> and the long-lived
///     <see cref="GrainActivation" /> shell.
///     Set by <see cref="LocalGrainCallInvoker" /> immediately after scope creation,
///     before the behavior is resolved from DI.
/// </summary>
public interface IActivationShellAccessor
{
    GrainActivation Shell { get; }
}