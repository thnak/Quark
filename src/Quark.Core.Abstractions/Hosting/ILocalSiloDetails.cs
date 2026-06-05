using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Exposes the identity and address of the local silo.
///     Inject into grain constructors or services to discover cluster membership metadata.
///     Drop-in equivalent of Orleans' <c>ILocalSiloDetails</c>.
/// </summary>
public interface ILocalSiloDetails
{
    /// <summary>The network address of this silo.</summary>
    SiloAddress SiloAddress { get; }

    /// <summary>Human-readable silo name (used in logs and diagnostics).</summary>
    string Name { get; }

    /// <summary>Cluster identifier shared by all silos in the same cluster.</summary>
    string ClusterId { get; }

    /// <summary>Service identifier that distinguishes multiple services sharing one cluster.</summary>
    string ServiceId { get; }
}
