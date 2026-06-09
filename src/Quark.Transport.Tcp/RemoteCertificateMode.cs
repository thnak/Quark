namespace Quark.Transport.Tcp;

/// <summary>How the local silo validates the remote silo's TLS certificate.</summary>
public enum RemoteCertificateMode
{
    /// <summary>No certificate is required from the remote side.</summary>
    NoCertificate,

    /// <summary>A certificate is accepted regardless of validity (useful for self-signed test certs).</summary>
    AllowAny,

    /// <summary>A valid, trusted certificate is required from the remote side.</summary>
    RequireCertificate,
}