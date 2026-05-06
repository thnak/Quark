namespace Quark.Core.Abstractions.Exceptions;

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