namespace Quark.Abstractions;

/// <summary>
/// Marker interface for actors that should have type-safe client proxies generated.
/// Methods in interfaces inheriting from IQuarkActor will have Protobuf message contracts
/// and client-side proxy implementations generated at compile-time.
/// </summary>
/// <remarks>
/// This interface enables AOT-compatible remote actor invocation with strong typing.
/// Actor implementations must also implement the concrete actor interface.
/// </remarks>
public interface IQuarkActor : IActor
{
}
