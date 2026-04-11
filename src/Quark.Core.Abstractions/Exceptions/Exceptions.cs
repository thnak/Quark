namespace Quark.Core.Abstractions.Exceptions;

/// <summary>Base exception type for all Quark framework errors.</summary>
public class QuarkException : Exception
{
    /// <inheritdoc/>
    public QuarkException() { }

    /// <inheritdoc/>
    public QuarkException(string message) : base(message) { }

    /// <inheritdoc/>
    public QuarkException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when a silo cannot be reached during a grain call.</summary>
public sealed class SiloUnavailableException : QuarkException
{
    /// <inheritdoc/>
    public SiloUnavailableException() : base("The target silo is unavailable.") { }

    /// <inheritdoc/>
    public SiloUnavailableException(string message) : base(message) { }

    /// <inheritdoc/>
    public SiloUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Thrown when a grain call fails (the grain threw an exception).</summary>
public sealed class GrainCallException : QuarkException
{
    /// <summary>The <see cref="GrainId"/> of the target grain.</summary>
    public GrainId? TargetGrain { get; }

    /// <inheritdoc/>
    public GrainCallException(string message) : base(message) { }

    /// <inheritdoc/>
    public GrainCallException(string message, GrainId targetGrain, Exception innerException)
        : base(message, innerException)
    {
        TargetGrain = targetGrain;
    }
}

/// <summary>Thrown when a grain with the requested key does not exist and cannot be created.</summary>
public sealed class GrainNotFoundException : QuarkException
{
    /// <summary>The identity that was not found.</summary>
    public GrainId? GrainId { get; }

    /// <inheritdoc/>
    public GrainNotFoundException(string message) : base(message) { }

    /// <inheritdoc/>
    public GrainNotFoundException(GrainId grainId)
        : base($"Grain '{grainId}' could not be found.")
    {
        GrainId = grainId;
    }
}
