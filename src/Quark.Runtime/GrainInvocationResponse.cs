namespace Quark.Runtime;

/// <summary>
///     Payload contract for a network-routed grain call response.
/// </summary>
public sealed record GrainInvocationResponse(bool Success, ReadOnlyMemory<byte> ResultPayload, string? Error);
