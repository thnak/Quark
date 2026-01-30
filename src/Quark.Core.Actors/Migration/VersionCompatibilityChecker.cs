using Quark.Abstractions.Migration;

namespace Quark.Core.Actors.Migration;

/// <summary>
/// Default implementation of IVersionCompatibilityChecker.
/// Determines version compatibility between different actor assembly versions.
/// </summary>
public sealed class VersionCompatibilityChecker : IVersionCompatibilityChecker
{
    /// <inheritdoc />
    public bool AreVersionsCompatible(
        string requestedVersion,
        string availableVersion,
        VersionCompatibilityMode compatibilityMode)
    {
        if (string.IsNullOrEmpty(requestedVersion) || string.IsNullOrEmpty(availableVersion))
        {
            return false;
        }

        // Exact match always compatible
        if (requestedVersion == availableVersion)
        {
            return true;
        }

        var requested = ParseVersion(requestedVersion);
        var available = ParseVersion(availableVersion);

        return compatibilityMode switch
        {
            VersionCompatibilityMode.Strict => false, // Already checked exact match above
            VersionCompatibilityMode.Patch => requested.Major == available.Major &&
                                             requested.Minor == available.Minor,
            VersionCompatibilityMode.Minor => requested.Major == available.Major,
            VersionCompatibilityMode.Major => true, // Any version compatible
            _ => false
        };
    }

    /// <inheritdoc />
    public string? GetBestMatchingVersion(
        string requestedVersion,
        IEnumerable<string> availableVersions,
        VersionCompatibilityMode compatibilityMode)
    {
        var versions = availableVersions.ToList();
        if (!versions.Any())
        {
            return null;
        }

        // First, try to find exact match
        if (versions.Contains(requestedVersion))
        {
            return requestedVersion;
        }

        // Filter compatible versions
        var compatibleVersions = versions
            .Where(v => AreVersionsCompatible(requestedVersion, v, compatibilityMode))
            .ToList();

        if (!compatibleVersions.Any())
        {
            return null;
        }

        // Sort by closeness to requested version (closest first) and return the best match
        // Note: Returns negative distances so OrderByDescending gives us the closest version
        var requested = ParseVersion(requestedVersion);
        return compatibleVersions
            .Select(v => (Version: v, Parsed: ParseVersion(v)))
            .OrderByDescending(v => CalculateVersionProximity(requested, v.Parsed))
            .Select(v => v.Version)
            .FirstOrDefault();
    }

    private static (int Major, int Minor, int Patch) ParseVersion(string version)
    {
        var parts = version.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var maj) ? maj : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var pat) ? pat : 0;
        return (major, minor, patch);
    }

    private static double CalculateVersionProximity(
        (int Major, int Minor, int Patch) requested,
        (int Major, int Minor, int Patch) available)
    {
        // Calculate "closeness" score - prefer versions closer to requested
        // Returns negative distance so OrderByDescending gives us the closest version
        var majorDiff = Math.Abs(requested.Major - available.Major);
        var minorDiff = Math.Abs(requested.Minor - available.Minor);
        var patchDiff = Math.Abs(requested.Patch - available.Patch);

        // Weight: major changes are most significant
        return -(majorDiff * 10000 + minorDiff * 100 + patchDiff);
    }
}
