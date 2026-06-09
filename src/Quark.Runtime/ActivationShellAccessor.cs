namespace Quark.Runtime;

internal sealed class ActivationShellAccessor : IActivationShellAccessor
{
    public GrainActivation Shell { get; set; } = null!;
}