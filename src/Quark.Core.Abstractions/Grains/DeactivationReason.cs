namespace Quark.Core.Abstractions.Grains;

/// <summary>Describes the reason a grain activation is being deactivated.</summary>
public sealed class DeactivationReason
{
    /// <summary>Standard idle timeout deactivation.</summary>
    public static readonly DeactivationReason IdleTimeout = new("IdleTimeout");

    /// <summary>Deactivated because the silo is shutting down.</summary>
    public static readonly DeactivationReason ShuttingDown = new("ShuttingDown");

    /// <summary>Explicitly requested by user code via <c>DeactivateOnIdle()</c>.</summary>
    public static readonly DeactivationReason ApplicationRequested = new("ApplicationRequested");

    /// <summary>Forced deactivation (e.g., migration or administrative eviction).</summary>
    public static readonly DeactivationReason Force = new("Force");

    /// <summary>Creates a custom deactivation reason.</summary>
    /// <param name="description">Human-readable description.</param>
    /// <param name="exception">Optional exception that triggered deactivation.</param>
    public DeactivationReason(string description, Exception? exception = null)
    {
        Description = description;
        Exception = exception;
    }

    /// <summary>Human-readable description of the deactivation reason.</summary>
    public string Description { get; }

    /// <summary>Optional exception that triggered deactivation.</summary>
    public Exception? Exception { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        Exception is null ? Description : $"{Description}: {Exception.Message}";
}

