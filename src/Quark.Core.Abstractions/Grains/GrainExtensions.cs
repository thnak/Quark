using System.Globalization;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Extension methods for extracting the primary key from a grain <em>reference</em> (proxy).
///     Drop-in equivalents of Orleans' <c>GrainExtensions</c> API.
///     Use the protected <c>GetPrimaryKey*()</c> methods on <see cref="Grain" /> when writing code
///     <em>inside</em> the grain — these extensions are for external callers holding a reference.
/// </summary>
public static class GrainExtensions
{
    /// <summary>Returns the <see cref="Guid" /> primary key of a grain reference.</summary>
    public static Guid GetPrimaryKey(this IGrainWithGuidKey grain)
        => Guid.ParseExact(((IGrainProxy)grain).GrainId.Key, "N");

    /// <summary>Returns the <see cref="long" /> primary key of a grain reference.</summary>
    public static long GetPrimaryKeyLong(this IGrainWithIntegerKey grain)
        => long.Parse(((IGrainProxy)grain).GrainId.Key, CultureInfo.InvariantCulture);

    /// <summary>Returns the <see cref="string" /> primary key of a grain reference.</summary>
    public static string GetPrimaryKeyString(this IGrainWithStringKey grain)
        => ((IGrainProxy)grain).GrainId.Key;
}
