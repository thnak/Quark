using Quark.Core.Abstractions.Lifecycle;

namespace Quark.Core.Abstractions;

/// <summary>
/// Represents the context of a single grain activation in the runtime.
/// Provided to grain base classes and framework components.
/// </summary>
public interface IGrainContext
{
    /// <summary>The stable identity of this grain.</summary>
    GrainId GrainId { get; }

    /// <summary>The lifecycle object for this activation (subscribe to be notified of activate/deactivate).</summary>
    ILifecycleSubject ObservableLifecycle { get; }

    /// <summary>The observable state for this activation's lifecycle.</summary>
    GrainActivationStatus ActivationStatus { get; }

    /// <summary>
    /// The grain factory scoped to this activation.
    /// Use this inside grain code to get references to other grains.
    /// Equivalent to Orleans' <c>GrainFactory</c> property.
    /// </summary>
    IGrainFactory GrainFactory { get; }

    /// <summary>
    /// The DI service provider scoped to this activation.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Requests that this activation be deactivated when it becomes idle.
    /// </summary>
    void Deactivate(DeactivationReason reason);
}

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

