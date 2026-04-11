namespace Quark.Core.Abstractions;

/// <summary>Describes the reason a grain activation is being deactivated.</summary>
public sealed class DeactivationReason
{
    /// <summary>Standard idle timeout deactivation.</summary>
    public static readonly DeactivationReason IdleTimeout = new("IdleTimeout");

    /// <summary>Deactivated because the silo is shutting down.</summary>
    public static readonly DeactivationReason ShuttingDown = new("ShuttingDown");

    /// <summary>Explicitly requested by user code via <c>DeactivateOnIdle()</c>.</summary>
    public static readonly DeactivationReason ApplicationRequested = new("ApplicationRequested");

    private DeactivationReason(string description)
    {
        Description = description;
    }

    /// <summary>Human-readable description of the deactivation reason.</summary>
    public string Description { get; }

    /// <inheritdoc/>
    public override string ToString() => Description;
}
