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
    /// Processes a complex object and returns a modified object.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    Task<ObjectClassValue> ProcessComplexObjectAsync(ObjectClassValue input);
    
    /// <summary>
    /// Performs a reset operation without return value.
    /// </summary>
    Task ResetAsync();
}


public class ObjectClassValue
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public DateTime Time { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public List<Info> Infos { get; set; } = new List<Info>();
}

public struct Info
{
    public int Id { get; set; }
    public string Description { get; set; }
}