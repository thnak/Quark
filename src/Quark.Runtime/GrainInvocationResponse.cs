namespace Quark.Runtime;

/// <summary>
/// Payload contract for a network-routed grain call response.
/// </summary>
public sealed record GrainInvocationResponse(bool Success, object? Result, string? Error);