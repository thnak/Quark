namespace Quark.Abstractions.Migration;

/// <summary>
/// Represents version information for an assembly or actor type.
/// </summary>
public sealed class AssemblyVersionInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssemblyVersionInfo"/> class.
    /// </summary>
    public AssemblyVersionInfo(string version, string? assemblyName = null)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        AssemblyName = assemblyName;
    }

    /// <summary>
    /// Gets the version string (e.g., "2.1.0").
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the assembly name (optional).
    /// </summary>
    public string? AssemblyName { get; }

    /// <summary>
    /// Parses the version string into major, minor, and patch components.
    /// </summary>
    public (int Major, int Minor, int Patch) ParseVersion()
    {
        var parts = Version.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var maj) ? maj : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var pat) ? pat : 0;
        return (major, minor, patch);
    }
}