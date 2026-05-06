namespace Quark.Core.Abstractions.Grains;

/// <summary>Grain that is identified by a compound key of a <see cref="Guid"/> and an optional string extension.</summary>
public interface IGrainWithGuidCompoundKey : IGrain
{
}