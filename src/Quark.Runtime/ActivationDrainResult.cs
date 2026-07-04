namespace Quark.Runtime;

/// <summary>
///     Result of a single drain pass on a <see cref="GrainActivation"/>'s mailbox.
/// </summary>
internal readonly record struct ActivationDrainResult(
    bool HasMoreWork,
    bool IsCompleted,
    int ItemsProcessed);