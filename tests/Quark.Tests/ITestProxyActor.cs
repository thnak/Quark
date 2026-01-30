using Quark.Abstractions;

namespace Quark.Tests;

/// <summary>
/// Test actor interface for proxy generation validation.
/// </summary>
public interface ITestProxyActor : IQuarkActor
{
    /// <summary>
    /// Increments a counter by the specified amount.
    /// </summary>
    Task IncrementAsync(int amount);

    /// <summary>
    /// Gets the current counter value.
    /// </summary>
    Task<int> GetCountAsync();

    /// <summary>
    /// Processes a message and returns a response.
    /// </summary>
    Task<string> ProcessMessageAsync(string message, int priority);

    /// <summary>
    /// Performs a reset operation without return value.
    /// </summary>
    Task ResetAsync();
}
