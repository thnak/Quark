using Quark.Abstractions;
using Quark.Abstractions.Converters;

namespace Quark.Tests;

/// <summary>
/// Test actor interface for proxy generation validation.
/// </summary>
public interface ITestProxyActor : IQuarkActor
{
    /// <summary>
    /// Increments a counter by the specified amount.
    /// </summary>
    [BinaryConverter(typeof(Int32Converter), ParameterName = "amount")]
    Task IncrementAsync(int amount);

    /// <summary>
    /// Gets the current counter value.
    /// </summary>
    [BinaryConverter(typeof(Int32Converter))] // Return value
    Task<int> GetCountAsync();

    /// <summary>
    /// Processes a message and returns a response.
    /// </summary>
    [BinaryConverter(typeof(StringConverter), ParameterName = "message", Order = 0)]
    [BinaryConverter(typeof(Int32Converter), ParameterName = "priority", Order = 1)]
    [BinaryConverter(typeof(StringConverter))] // Return value
    Task<string> ProcessMessageAsync(string message, int priority);

    /// <summary>
    /// Processes a complex object and returns a modified object.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    [BinaryConverter(typeof(ObjectClassValueConverter), ParameterName = "input")]
    [BinaryConverter(typeof(ObjectClassValueConverter))] // Return value
    Task<ObjectClassValue> ProcessComplexObjectAsync(ObjectClassValue input);
    
    /// <summary>
    /// Performs a reset operation without return value.
    /// </summary>
    Task ResetAsync();
}