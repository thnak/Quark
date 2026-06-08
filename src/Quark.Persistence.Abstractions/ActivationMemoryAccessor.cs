using Quark.Core.Abstractions.Hosting;

namespace Quark.Persistence.Abstractions;

/// <summary>
///     Engine-internal. Scoped accessor that projects a shell-owned
///     <see cref="StateHolder{TState}" /> as <see cref="IActivationMemory{TState}" />.
/// </summary>
public sealed class ActivationMemoryAccessor<TState>(StateHolder<TState> holder)
    : IActivationMemory<TState>
    where TState : class, new()
{
    public TState Value => holder.Value;
}
