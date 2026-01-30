using Quark.Abstractions.Migration;
using Quark.Core.Actors.Migration;
using Xunit;

namespace Quark.Tests;

public class VersionCompatibilityCheckerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", VersionCompatibilityMode.Strict, true)]
    [InlineData("1.0.0", "1.0.1", VersionCompatibilityMode.Strict, false)]
    [InlineData("1.0.0", "1.0.1", VersionCompatibilityMode.Patch, true)]
    [InlineData("1.0.0", "1.1.0", VersionCompatibilityMode.Patch, false)]
    [InlineData("1.0.0", "1.1.0", VersionCompatibilityMode.Minor, true)]
    [InlineData("1.0.0", "2.0.0", VersionCompatibilityMode.Minor, false)]
    [InlineData("1.0.0", "2.0.0", VersionCompatibilityMode.Major, true)]
    public void AreVersionsCompatible_ReturnsExpectedResult(
        string requestedVersion,
        string availableVersion,
        VersionCompatibilityMode mode,
        bool expectedResult)
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();

        // Act
        var result = checker.AreVersionsCompatible(requestedVersion, availableVersion, mode);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void AreVersionsCompatible_ExactMatch_AlwaysReturnsTrue()
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();
        const string version = "2.5.3";

        // Act & Assert
        Assert.True(checker.AreVersionsCompatible(version, version, VersionCompatibilityMode.Strict));
        Assert.True(checker.AreVersionsCompatible(version, version, VersionCompatibilityMode.Patch));
        Assert.True(checker.AreVersionsCompatible(version, version, VersionCompatibilityMode.Minor));
        Assert.True(checker.AreVersionsCompatible(version, version, VersionCompatibilityMode.Major));
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("1.0.0", null)]
    [InlineData("", "1.0.0")]
    [InlineData("1.0.0", "")]
    public void AreVersionsCompatible_WithNullOrEmpty_ReturnsFalse(
        string? requestedVersion,
        string? availableVersion)
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();

        // Act
        var result = checker.AreVersionsCompatible(
            requestedVersion!,
            availableVersion!,
            VersionCompatibilityMode.Major);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetBestMatchingVersion_ReturnsExactMatch_WhenAvailable()
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();
        var availableVersions = new[] { "1.0.0", "1.0.1", "1.1.0", "2.0.0" };
        const string requestedVersion = "1.0.1";

        // Act
        var result = checker.GetBestMatchingVersion(
            requestedVersion,
            availableVersions,
            VersionCompatibilityMode.Minor);

        // Assert
        Assert.Equal("1.0.1", result);
    }

    [Fact]
    public void GetBestMatchingVersion_ReturnsClosestCompatible_WhenExactMatchNotAvailable()
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();
        var availableVersions = new[] { "1.0.0", "1.0.1", "1.1.0", "2.0.0" };
        const string requestedVersion = "1.0.5";

        // Act - Patch compatibility: should match 1.0.x versions
        var result = checker.GetBestMatchingVersion(
            requestedVersion,
            availableVersions,
            VersionCompatibilityMode.Patch);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("1.0.", result);
    }

    [Fact]
    public void GetBestMatchingVersion_ReturnsNull_WhenNoCompatibleVersionExists()
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();
        var availableVersions = new[] { "1.0.0", "1.0.1" };
        const string requestedVersion = "2.0.0";

        // Act - Strict mode: only exact match allowed
        var result = checker.GetBestMatchingVersion(
            requestedVersion,
            availableVersions,
            VersionCompatibilityMode.Strict);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetBestMatchingVersion_ReturnsNull_WhenNoVersionsAvailable()
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();
        var availableVersions = Array.Empty<string>();
        const string requestedVersion = "1.0.0";

        // Act
        var result = checker.GetBestMatchingVersion(
            requestedVersion,
            availableVersions,
            VersionCompatibilityMode.Major);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetBestMatchingVersion_MinorCompatibility_MatchesSameMajorVersion()
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();
        var availableVersions = new[] { "1.0.0", "1.5.0", "2.0.0", "2.1.0" };
        const string requestedVersion = "1.3.0";

        // Act - Minor compatibility: should match 1.x versions
        var result = checker.GetBestMatchingVersion(
            requestedVersion,
            availableVersions,
            VersionCompatibilityMode.Minor);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("1.", result);
    }

    [Fact]
    public void GetBestMatchingVersion_PatchCompatibility_MatchesSameMajorMinor()
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();
        var availableVersions = new[] { "1.0.0", "1.0.5", "1.1.0", "2.0.0" };
        const string requestedVersion = "1.0.3";

        // Act - Patch compatibility: should match 1.0.x versions
        var result = checker.GetBestMatchingVersion(
            requestedVersion,
            availableVersions,
            VersionCompatibilityMode.Patch);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("1.0.", result);
    }

    [Fact]
    public void GetBestMatchingVersion_MajorCompatibility_ReturnsAnyVersion()
    {
        // Arrange
        var checker = new VersionCompatibilityChecker();
        var availableVersions = new[] { "1.0.0", "2.0.0", "3.0.0" };
        const string requestedVersion = "5.0.0";

        // Act - Major compatibility: any version is compatible
        var result = checker.GetBestMatchingVersion(
            requestedVersion,
            availableVersions,
            VersionCompatibilityMode.Major);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result, availableVersions);
    }
}
