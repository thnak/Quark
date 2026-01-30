namespace Quark.Placement.Abstractions;

/// <summary>
/// GPU backend types for actor acceleration.
/// </summary>
public enum GpuBackend
{
    /// <summary>
    /// Automatically detect the best available backend.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// NVIDIA CUDA backend.
    /// </summary>
    Cuda = 1,

    /// <summary>
    /// OpenCL backend (cross-platform).
    /// </summary>
    OpenCL = 2
}
