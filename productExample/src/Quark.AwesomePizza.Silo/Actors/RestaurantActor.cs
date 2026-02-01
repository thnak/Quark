using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Silo.Actors;

/// <summary>
/// Actor that aggregates restaurant-level metrics and operations.
/// Demonstrates multi-stream aggregation pattern.
/// </summary>
[Actor(Name = "Restaurant", Reentrant = false)]
public class RestaurantActor : ActorBase
{
    private RestaurantMetrics? _metrics;
    private readonly List<string> _activeOrderIds = new();
    private readonly List<string> _driverIds = new();
    private readonly List<string> _chefIds = new();

    public RestaurantActor(string actorId, IActorFactory? actorFactory = null) 
        : base(actorId, actorFactory)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // In full implementation, load state and subscribe to streams
        _metrics = new RestaurantMetrics(
            RestaurantId: ActorId,
            ActiveOrders: 0,
            CompletedOrders: 0,
            AvailableDrivers: 0,
            BusyDrivers: 0,
            AvailableChefs: 0,
            BusyChefs: 0,
            AverageDeliveryTime: 0,
            LastUpdated: DateTime.UtcNow);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers a new order with the restaurant.
    /// </summary>
    public async Task<RestaurantMetrics> RegisterOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (_metrics == null)
            throw new InvalidOperationException("Restaurant not initialized");

        _activeOrderIds.Add(orderId);

        _metrics = _metrics with
        {
            ActiveOrders = _activeOrderIds.Count,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        return _metrics;
    }

    /// <summary>
    /// Marks an order as complete.
    /// </summary>
    public Task<RestaurantMetrics> CompleteOrderAsync(
        string orderId,
        TimeSpan deliveryTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (_metrics == null)
            throw new InvalidOperationException("Restaurant not initialized");

        _activeOrderIds.Remove(orderId);

        // Update average delivery time (simple moving average)
        var totalOrders = _metrics.CompletedOrders + 1;
        var newAverage = (_metrics.AverageDeliveryTime * _metrics.CompletedOrders + (decimal)deliveryTime.TotalMinutes) / totalOrders;

        _metrics = _metrics with
        {
            ActiveOrders = _activeOrderIds.Count,
            CompletedOrders = _metrics.CompletedOrders + 1,
            AverageDeliveryTime = newAverage,
            LastUpdated = DateTime.UtcNow
        };

        // TODO: await SaveStateAsync()
        return Task.FromResult(_metrics);
    }

    /// <summary>
    /// Registers a driver with the restaurant.
    /// </summary>
    public Task<RestaurantMetrics> RegisterDriverAsync(
        string driverId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driverId);

        if (_metrics == null)
            throw new InvalidOperationException("Restaurant not initialized");

        if (!_driverIds.Contains(driverId))
        {
            _driverIds.Add(driverId);
        }

        return RefreshDriverMetricsAsync(cancellationToken);
    }

    /// <summary>
    /// Registers a chef with the restaurant.
    /// </summary>
    public Task<RestaurantMetrics> RegisterChefAsync(
        string chefId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chefId);

        if (_metrics == null)
            throw new InvalidOperationException("Restaurant not initialized");

        if (!_chefIds.Contains(chefId))
        {
            _chefIds.Add(chefId);
        }

        return RefreshChefMetricsAsync(cancellationToken);
    }

    /// <summary>
    /// Gets current restaurant metrics.
    /// </summary>
    public async Task<RestaurantMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (_metrics == null)
            throw new InvalidOperationException("Restaurant not initialized");

        // Refresh metrics from actors
        await RefreshDriverMetricsAsync(cancellationToken);
        await RefreshChefMetricsAsync(cancellationToken);

        return _metrics;
    }

    /// <summary>
    /// Gets a list of available drivers for assignment.
    /// </summary>
    public async Task<List<string>> GetAvailableDriversAsync(CancellationToken cancellationToken = default)
    {
        var availableDrivers = new List<string>();

        foreach (var driverId in _driverIds)
        {
            // TODO: Get DriverActor and check availability
            // var driverActor = GetActor<DriverActor>(driverId);
            // var isAvailable = await driverActor.IsAvailableAsync(cancellationToken);
            // if (isAvailable)
            // {
            //     availableDrivers.Add(driverId);
            // }
        }

        // For now, return all registered drivers
        return availableDrivers;
    }

    /// <summary>
    /// Gets a list of available chefs.
    /// </summary>
    public async Task<List<string>> GetAvailableChefsAsync(CancellationToken cancellationToken = default)
    {
        var availableChefs = new List<string>();

        foreach (var chefId in _chefIds)
        {
            // TODO: Get ChefActor and check availability
            // var chefActor = GetActor<ChefActor>(chefId);
            // var isAvailable = await chefActor.IsAvailableAsync(cancellationToken);
            // if (isAvailable)
            // {
            //     availableChefs.Add(chefId);
            // }
        }

        // For now, return all registered chefs
        return availableChefs;
    }

    /// <summary>
    /// Finds the best available driver for an order based on location.
    /// </summary>
    public async Task<string?> FindBestDriverAsync(
        GpsLocation deliveryAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliveryAddress);

        // TODO: Implement proper driver selection algorithm
        // - Get all available drivers
        // - Calculate distance from delivery address
        // - Consider driver ratings, current load, etc.
        // - Return driver with best score

        var availableDrivers = await GetAvailableDriversAsync(cancellationToken);
        return availableDrivers.FirstOrDefault();
    }

    /// <summary>
    /// Refreshes driver metrics from DriverActors.
    /// </summary>
    private async Task<RestaurantMetrics> RefreshDriverMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (_metrics == null)
            return _metrics!;

        int available = 0;
        int busy = 0;

        foreach (var driverId in _driverIds)
        {
            // TODO: Query each DriverActor
            // var driverActor = GetActor<DriverActor>(driverId);
            // var state = await driverActor.GetStateAsync(cancellationToken);
            // if (state?.Status == DriverStatus.Available)
            //     available++;
            // else if (state?.Status == DriverStatus.Busy)
            //     busy++;
        }

        // For now, just count registered drivers
        available = _driverIds.Count;

        _metrics = _metrics with
        {
            AvailableDrivers = available,
            BusyDrivers = busy,
            LastUpdated = DateTime.UtcNow
        };

        return _metrics;
    }

    /// <summary>
    /// Refreshes chef metrics from ChefActors.
    /// </summary>
    private async Task<RestaurantMetrics> RefreshChefMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (_metrics == null)
            return _metrics!;

        int available = 0;
        int busy = 0;

        foreach (var chefId in _chefIds)
        {
            // TODO: Query each ChefActor
            // var chefActor = GetActor<ChefActor>(chefId);
            // var state = await chefActor.GetStateAsync(cancellationToken);
            // if (state?.Status == ChefStatus.Available)
            //     available++;
            // else if (state?.Status == ChefStatus.Busy)
            //     busy++;
        }

        // For now, just count registered chefs
        available = _chefIds.Count;

        _metrics = _metrics with
        {
            AvailableChefs = available,
            BusyChefs = busy,
            LastUpdated = DateTime.UtcNow
        };

        return _metrics;
    }
}
