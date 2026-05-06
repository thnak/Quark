using Quark.Core.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Payload contract for a network-routed grain call.
/// </summary>
public sealed record GrainInvocationRequest(GrainId GrainId, uint MethodId, object?[]? Arguments);