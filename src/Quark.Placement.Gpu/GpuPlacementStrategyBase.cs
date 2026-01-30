using System.Collections.Concurrent;
using Quark.Placement.Abstractions;

namespace Quark.Placement.Gpu;

/// <summary>
/// Base implementation of GPU-affinity actor placement strategy.
/// This provides common logic for GPU device selection and actor tracking.
/// Hardware-specific implementations should inherit from this class.
/// </summary>
public abstract class GpuPlacementStrategyBase : IGpuPlacementStrategy
{
    private readonly GpuAccelerationOptions _options;
    private readonly ConcurrentDictionary<string, int> _actorToDeviceMap = new();
    private readonly ConcurrentDictionary<int, int> _deviceActorCounts = new();
    private int _nextDeviceRoundRobin = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuPlacementStrategyBase"/> class.
    /// </summary>
    /// <param name="options">Configuration options for GPU acceleration.</param>
    protected GpuPlacementStrategyBase(GpuAccelerationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public virtual async Task<int?> GetPreferredGpuDeviceAsync(Type actorType, string actorId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        // Check if specific actor types are configured
        if (_options.AcceleratedActorTypes.Count > 0 && 
            !_options.AcceleratedActorTypes.Contains(actorType.Name))
        {
            return _options.AllowCpuFallback ? null : 0;
        }

        // Check if actor already has a device assigned
        if (_actorToDeviceMap.TryGetValue(actorId, out var existingDevice))
            return existingDevice;

        // Get available devices
        var devices = await GetAvailableGpuDevicesAsync(cancellationToken);
        var availableDevices = devices.Where(d => d.IsAvailable).ToList();

        if (availableDevices.Count == 0)
            return _options.AllowCpuFallback ? null : throw new InvalidOperationException("No GPU devices available");

        // Select device based on strategy
        var selectedDevice = _options.DeviceSelectionStrategy switch
        {
            "LeastUtilized" => SelectLeastUtilizedDevice(availableDevices),
            "LeastMemoryUsed" => SelectLeastMemoryUsedDevice(availableDevices),
            "RoundRobin" => SelectRoundRobinDevice(availableDevices),
            "FirstAvailable" => availableDevices[0].DeviceId,
            _ => SelectLeastUtilizedDevice(availableDevices)
        };

        return selectedDevice;
    }

    /// <inheritdoc/>
    public abstract Task<IReadOnlyCollection<GpuDeviceInfo>> GetAvailableGpuDevicesAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual Task OnActorActivatedAsync(Type actorType, string actorId, int gpuDeviceId, CancellationToken cancellationToken = default)
    {
        _actorToDeviceMap[actorId] = gpuDeviceId;
        _deviceActorCounts.AddOrUpdate(gpuDeviceId, 1, (_, count) => count + 1);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task OnActorDeactivatedAsync(Type actorType, string actorId, int gpuDeviceId, CancellationToken cancellationToken = default)
    {
        _actorToDeviceMap.TryRemove(actorId, out _);
        _deviceActorCounts.AddOrUpdate(gpuDeviceId, 0, (_, count) => Math.Max(0, count - 1));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Selects the GPU device with the lowest compute utilization.
    /// </summary>
    /// <param name="devices">Available GPU devices.</param>
    /// <returns>The selected GPU device ID.</returns>
    protected int SelectLeastUtilizedDevice(List<GpuDeviceInfo> devices)
    {
        var availableDevices = devices
            .Where(d => d.UtilizationPercent < _options.MaxGpuComputeUtilization * 100)
            .ToList();

        if (availableDevices.Count == 0)
            availableDevices = devices;

        return availableDevices
            .OrderBy(d => d.UtilizationPercent)
            .ThenBy(d => d.ActiveActorCount)
            .First()
            .DeviceId;
    }

    /// <summary>
    /// Selects the GPU device with the most available memory.
    /// </summary>
    /// <param name="devices">Available GPU devices.</param>
    /// <returns>The selected GPU device ID.</returns>
    protected int SelectLeastMemoryUsedDevice(List<GpuDeviceInfo> devices)
    {
        var availableDevices = devices
            .Where(d => (double)d.AvailableMemoryBytes / Math.Max(1, d.TotalMemoryBytes) > (1 - _options.MaxGpuMemoryUtilization))
            .ToList();

        if (availableDevices.Count == 0)
            availableDevices = devices;

        return availableDevices
            .OrderByDescending(d => d.AvailableMemoryBytes)
            .ThenBy(d => d.ActiveActorCount)
            .First()
            .DeviceId;
    }

    /// <summary>
    /// Selects the next GPU device using round-robin.
    /// </summary>
    /// <param name="devices">Available GPU devices.</param>
    /// <returns>The selected GPU device ID.</returns>
    protected int SelectRoundRobinDevice(List<GpuDeviceInfo> devices)
    {
        var deviceList = devices.OrderBy(d => d.DeviceId).ToList();
        if (deviceList.Count == 0)
            return 0;

        var index = Interlocked.Increment(ref _nextDeviceRoundRobin) % deviceList.Count;
        return deviceList[index].DeviceId;
    }
}
