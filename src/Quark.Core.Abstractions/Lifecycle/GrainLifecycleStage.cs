namespace Quark.Core.Abstractions.Lifecycle;

/// <summary>
/// Well-known stage numbers for the grain activation lifecycle.
/// </summary>
public static class GrainLifecycleStage
{
    /// <summary>First stage of activation.</summary>
    public const int First = int.MinValue;

    /// <summary>Set up grain state storage.</summary>
    public const int SetupState = 1_000;

    /// <summary>Activate the grain — <c>OnActivateAsync</c> is called here.</summary>
    public const int Activate = 2_000;

    /// <summary>Last stage of activation.</summary>
    public const int Last = int.MaxValue;
}
