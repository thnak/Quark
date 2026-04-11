namespace Quark.Core.Abstractions.Lifecycle;

/// <summary>
/// Well-known stage numbers for the silo service lifecycle.
/// Higher numbers start later and stop earlier.
/// </summary>
public static class ServiceLifecycleStage
{
    /// <summary>First stage — used for infrastructure setup.</summary>
    public const int First = int.MinValue;

    /// <summary>Runtime infrastructure (transport, membership).</summary>
    public const int RuntimeInitialize = 1_000;

    /// <summary>Grain directory and placement.</summary>
    public const int RuntimeGrainServices = 2_000;

    /// <summary>Inbound/outbound message pumps.</summary>
    public const int RuntimeMessageCenters = 3_000;

    /// <summary>Application-level services.</summary>
    public const int Application = 10_000;

    /// <summary>Last stage — used for final cleanup.</summary>
    public const int Last = int.MaxValue;
}
