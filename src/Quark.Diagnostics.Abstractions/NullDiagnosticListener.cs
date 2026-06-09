namespace Quark.Diagnostics.Abstractions;

/// <summary>
///     No-op <see cref="IQuarkDiagnosticListener" /> used as the default when no listener is registered.
///     All methods are empty and will be elided by the JIT.
/// </summary>
public sealed class NullDiagnosticListener : IQuarkDiagnosticListener
{
    public static readonly NullDiagnosticListener Instance = new();
}
