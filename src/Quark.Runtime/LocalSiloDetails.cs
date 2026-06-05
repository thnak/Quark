using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Concrete <see cref="ILocalSiloDetails" /> that reads silo identity from <see cref="SiloRuntimeOptions" />.
/// </summary>
internal sealed class LocalSiloDetails(IOptions<SiloRuntimeOptions> options) : ILocalSiloDetails
{
    private readonly SiloRuntimeOptions _options = options.Value;

    /// <inheritdoc />
    public SiloAddress SiloAddress => _options.SiloAddress;

    /// <inheritdoc />
    public string Name => _options.SiloName;

    /// <inheritdoc />
    public string ClusterId => _options.ClusterId;

    /// <inheritdoc />
    public string ServiceId => _options.ServiceId;
}
