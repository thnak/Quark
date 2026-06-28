using Quark.Core.Abstractions.Hosting;

namespace Quark.Persistence.Abstractions;

/// <summary>
///     Engine-internal. Scoped DI accessor that projects a shell-owned
///     <see cref="EagerActivationMemoryHolder{T}" /> as <see cref="IEagerActivationMemory{T}" />.
///     This thin wrapper is what gets registered as a scoped service; the holder itself is owned
///     by the shell and is not disposed by the DI container.
/// </summary>
public sealed class EagerActivationMemoryAccessor<T>(EagerActivationMemoryHolder<T> holder)
    : IEagerActivationMemory<T>
    where T : class
{
    public IEagerActivationMemory<T> Load(Func<IServiceProvider, CancellationToken, ValueTask<T>> factory)
    {
        holder.Load(factory);
        return this;
    }

    public IEagerActivationMemory<T> Destroy(Func<T, ValueTask> cleanup)
    {
        holder.Destroy(cleanup);
        return this;
    }

    public T Value => holder.Value;

    public bool IsInitialized => holder.IsInitialized;
}
