using System.Net;

namespace Quark.Runtime;

/// <summary>
/// Identifies the network endpoint of a single Quark silo.
/// </summary>
public readonly struct SiloAddress : IEquatable<SiloAddress>
{
    /// <summary>DNS host name or IP address.</summary>
    public string Host { get; }

    /// <summary>TCP port the silo listens on for grain traffic.</summary>
    public int Port { get; }

    /// <summary>Generation counter that disambiguates silos that reuse the same endpoint.</summary>
    public int Generation { get; }

    /// <summary>Initialises a new <see cref="SiloAddress"/>.</summary>
    public SiloAddress(string host, int port, int generation = 0)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
        Port = port;
        Generation = generation;
    }

    /// <summary>Creates a loopback address for local testing.</summary>
    public static SiloAddress Loopback(int port, int generation = 0) =>
        new(IPAddress.Loopback.ToString(), port, generation);

    /// <inheritdoc/>
    public override string ToString() => $"{Host}:{Port}@{Generation}";

    /// <summary>Parses a string produced by <see cref="ToString"/>.</summary>
    public static SiloAddress Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int atIdx = value.LastIndexOf('@');
        int generation = atIdx >= 0 ? int.Parse(value[(atIdx + 1)..]) : 0;
        string hostPort = atIdx >= 0 ? value[..atIdx] : value;
        int colonIdx = hostPort.LastIndexOf(':');
        if (colonIdx < 0) throw new FormatException($"Invalid SiloAddress: '{value}'");
        string host = hostPort[..colonIdx];
        int port = int.Parse(hostPort[(colonIdx + 1)..]);
        return new SiloAddress(host, port, generation);
    }

    /// <inheritdoc/>
    public bool Equals(SiloAddress other) =>
        Host == other.Host && Port == other.Port && Generation == other.Generation;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SiloAddress s && Equals(s);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Host, Port, Generation);

    /// <inheritdoc/>
    public static bool operator ==(SiloAddress left, SiloAddress right) => left.Equals(right);

    /// <inheritdoc/>
    public static bool operator !=(SiloAddress left, SiloAddress right) => !left.Equals(right);
}
