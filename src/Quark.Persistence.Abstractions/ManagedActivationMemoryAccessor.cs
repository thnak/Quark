using Quark.Core.Abstractions.Hosting;

namespace Quark.Persistence.Abstractions;

/// <summary>
///     Engine-internal. Scoped DI accessor that projects a shell-owned
///     <see cref="ManagedActivationMemoryHolder{T}" /> as <see cref="IManagedActivationMemory{T}" />.
///     This thin wrapper is what gets registered as a scoped service; the holder itself is owned
///     by the shell and is not disposed by the DI container.
/// </summary>
public sealed class ManagedActivationMemoryAccessor<T>(ManagedActivationMemoryHolder<T> holder)
    : IManagedActivationMemory<T>
    where T : class
{
    public IManagedActivationMemory<T> Init(Func<Task<T>> factory)
    {
        holder.Init(factory);
        return this;
    }

    public IManagedActivationMemory<T> Destroy(Func<T, Task> cleanup)
    {
        holder.Destroy(cleanup);
        return this;
    }

    public ValueTask<T> GetAsync(CancellationToken ct = default) => holder.GetAsync(ct);

    public bool IsInitialized => holder.IsInitialized;
}
