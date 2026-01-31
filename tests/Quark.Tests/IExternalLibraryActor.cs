namespace Quark.Tests;

/// <summary>
/// Test actor interface that does NOT inherit from IQuarkActor.
/// This simulates an external library interface that cannot be modified.
/// Will be registered via QuarkActorContext for proxy generation.
/// </summary>
public interface IExternalLibraryActor
{
    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    string ActorId { get; }

    /// <summary>
    /// Performs a calculation and returns the result.
    /// </summary>
    Task<int> CalculateAsync(int x, int y);

    /// <summary>
    /// Performs an operation without a return value.
    /// </summary>
    Task PerformOperationAsync(string operation);

    /// <summary>
    /// Gets data by ID.
    /// </summary>
    Task<string> GetDataAsync(string id);
}
