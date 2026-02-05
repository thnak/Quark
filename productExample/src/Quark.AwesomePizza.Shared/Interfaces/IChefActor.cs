using Quark.Abstractions;
using Quark.Abstractions.Converters;
using Quark.AwesomePizza.Shared.Models;
using Quark.AwesomePizza.Shared.Converters;

namespace Quark.AwesomePizza.Shared.Interfaces;

/// <summary>
/// Interface for Chef Actor - manages individual chef workload.
/// This is exposed to Gateway and MQTT for actor proxy calls via IClusterClient.
/// </summary>
public interface IChefActor : IQuarkActor
{
    /// <summary>
    /// Initializes a new chef.
    /// </summary>
    [BinaryConverter(typeof(StringConverter), ParameterName = "name", Order = 0)]
    [BinaryConverter(typeof(ChefStateConverter))]
    Task<ChefState> InitializeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current chef state.
    /// </summary>
    [BinaryConverter(typeof(ChefStateConverter))]
    Task<ChefState?> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns an order to the chef.
    /// </summary>
    [BinaryConverter(typeof(StringConverter), ParameterName = "orderId", Order = 0)]
    [BinaryConverter(typeof(ChefStateConverter))]
    Task<ChefState> AssignOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the current order as completed.
    /// </summary>
    [BinaryConverter(typeof(ChefStateConverter))]
    Task<ChefState> CompleteOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates chef status.
    /// </summary>
    [BinaryConverter(typeof(Int32Converter), ParameterName = "status", Order = 0)]
    [BinaryConverter(typeof(ChefStateConverter))]
    Task<ChefState> UpdateStatusAsync(ChefStatus status, CancellationToken cancellationToken = default);
}
