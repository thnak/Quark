namespace Quark.Core.Abstractions;

/// <summary>Grain that is identified by a single string key.</summary>
public interface IGrainWithStringKey : IGrain
{
}

/// <summary>Grain that is identified by a 64-bit integer key.</summary>
public interface IGrainWithIntegerKey : IGrain
{
}

/// <summary>Grain that is identified by a <see cref="Guid"/> key.</summary>
public interface IGrainWithGuidKey : IGrain
{
}

/// <summary>Grain that is identified by a compound key of a 64-bit integer and an optional string extension.</summary>
public interface IGrainWithIntegerCompoundKey : IGrain
{
}

/// <summary>Grain that is identified by a compound key of a <see cref="Guid"/> and an optional string extension.</summary>
public interface IGrainWithGuidCompoundKey : IGrain
{
}
