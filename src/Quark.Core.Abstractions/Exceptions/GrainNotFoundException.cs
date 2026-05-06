using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Exceptions;

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