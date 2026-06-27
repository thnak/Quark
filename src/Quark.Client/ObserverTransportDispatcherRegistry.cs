using Quark.Runtime;

namespace Quark.Client;

/// <summary>
///     Maps observer <see cref="Quark.Core.Abstractions.Identity.GrainType" />s to their generated
///     <see cref="Quark.Core.Abstractions.Hosting.ITransportGrainDispatcher" /> implementations.
///     Used by the TCP client to dispatch incoming
///     <see cref="Quark.Transport.Abstractions.MessageType.ObserverInvoke" /> frames to local
///     observer implementations. Shares its storage and <c>Register</c>/<c>TryGet</c> API with the
///     silo-side registry via <see cref="TransportDispatcherRegistry" />.
/// </summary>
public sealed class ObserverTransportDispatcherRegistry : TransportDispatcherRegistry;
