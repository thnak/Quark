namespace Quark.Core.Abstractions.Grains;

/// <summary>Describes the reason a grain activation is being deactivated.</summary>
public sealed class DeactivationReason
{
    /// <summary>Standard idle timeout deactivation.</summary>
    public static readonly DeactivationReason IdleTimeout = new("IdleTimeout");

    /// <summary>Deactivated because the silo is shutting down.</summary>
    public static readonly DeactivationReason ShuttingDown = new("ShuttingDown");

    /// <summary>Explicitly requested by user code via <c>DeactivateOnIdle()</c>.</summary>
    public static readonly DeactivationReason ApplicationRequested = new("ApplicationRequested", cascades: true);

    /// <summary>Forced deactivation (e.g., migration or administrative eviction).</summary>
    public static readonly DeactivationReason Force = new("Force", cascades: true);

    /// <summary>The parent grain was terminated; propagated to cascade-mode children.</summary>
    public static readonly DeactivationReason ParentTerminated = new("ParentTerminated", cascades: true);

    /// <summary>Creates a custom deactivation reason.</summary>
    /// <param name="description">Human-readable description.</param>
    /// <param name="exception">Optional exception that triggered deactivation.</param>
    /// <param name="cascades">
    ///     When <see langword="true"/> the runtime propagates termination to all
    ///     <see cref="Quark.Core.Abstractions.Hosting.ChildTerminationMode.Cascade"/> children
    ///     after the parent's own <c>OnDeactivateAsync</c> completes.
    /// </param>
    public DeactivationReason(string description, Exception? exception = null, bool cascades = false)
    {
        Description = description;
        Exception = exception;
        CascadesToChildren = cascades;
    }

    /// <summary>Human-readable description of the deactivation reason.</summary>
    public string Description { get; }

    /// <summary>Optional exception that triggered deactivation.</summary>
    public Exception? Exception { get; }

    /// <summary>
    ///     When <see langword="true"/> the runtime propagates termination to all
    ///     <see cref="Quark.Core.Abstractions.Hosting.ChildTerminationMode.Cascade"/> children
    ///     after the parent's own deactivation hook completes.
    ///     <see langword="false"/> for passive reasons (idle timeout, silo shutdown) — those
    ///     do not kill live children that belong to an independent lifecycle.
    /// </summary>
    public bool CascadesToChildren { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return Exception is null ? Description : $"{Description}: {Exception.Message}";
    }
}
