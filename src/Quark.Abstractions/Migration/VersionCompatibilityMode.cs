namespace Quark.Abstractions.Migration;

/// <summary>
/// Represents version compatibility mode for actor placement.
/// </summary>
public enum VersionCompatibilityMode
{
    /// <summary>
    /// Strict version matching - only exact versions are compatible.
    /// </summary>
    Strict,

    /// <summary>
    /// Patch version compatibility - versions with the same major and minor versions are compatible (e.g., v2.1.3 compatible with v2.1.0).
    /// </summary>
    Patch,

    /// <summary>
    /// Minor version compatibility - any version with the same major version is compatible (e.g., v2.0.0 compatible with v2.5.0).
    /// </summary>
    Minor,

    /// <summary>
    /// Major version compatibility - any version compatible.
    /// </summary>
    Major
}