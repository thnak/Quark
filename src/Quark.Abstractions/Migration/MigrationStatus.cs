namespace Quark.Abstractions.Migration;

/// <summary>
/// Represents the status of an actor migration operation.
/// </summary>
public enum MigrationStatus
{
    /// <summary>
    /// Migration has not started yet.
    /// </summary>
    NotStarted,

    /// <summary>
    /// Migration is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Migration completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Migration failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Migration was cancelled.
    /// </summary>
    Cancelled
}