using Quark.Abstractions;
using Quark.Abstractions.Converters;

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
    [BinaryConverter(typeof(Int32Converter), ParameterName = "x", Order = 0)]
    [BinaryConverter(typeof(Int32Converter), ParameterName = "y", Order = 1)]
    [BinaryConverter(typeof(Int32Converter))] // Return value
    Task<int> CalculateAsync(int x, int y);

    /// <summary>
    /// Performs an operation without a return value.
    /// </summary>
    [BinaryConverter(typeof(StringConverter), ParameterName = "operation")]
    Task PerformOperationAsync(string operation);

    /// <summary>
    /// Gets data by ID.
    /// </summary>
    [BinaryConverter(typeof(StringConverter), ParameterName = "id")]
    [BinaryConverter(typeof(StringConverter))] // Return value
    Task<string> GetDataAsync(string id);
}
