namespace Quark.Core.Abstractions;

/// <summary>Current state of a grain activation.</summary>
public enum GrainActivationStatus
{
    /// <summary>Activation is starting up.</summary>
    Activating,

    /// <summary>Activation is fully active and processing messages.</summary>
    Active,

    /// <summary>Activation is shutting down.</summary>
    Deactivating,

    /// <summary>Activation is fully stopped.</summary>
    Inactive,
}