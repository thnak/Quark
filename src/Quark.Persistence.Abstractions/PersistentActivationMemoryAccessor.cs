using Quark.Core.Abstractions.Hosting;

namespace Quark.Persistence.Abstractions;

/// <summary>
///     Engine-internal. Scoped accessor that projects a shell-owned
///     <see cref="StateHolder{TState}" /> as <see cref="IPersistentActivationMemory{TState}" />,
///     delegating load/save/clear to the registered <see cref="IStorage{TState}" /> provider.
/// </summary>
public sealed class PersistentActivationMemoryAccessor<TState>(
    StateHolder<TState> holder,
    IStorage<TState> storage,
    ICallContext ctx,
    string stateName)
    : IPersistentActivationMemory<TState>
    where TState : class, new()
{
    public TState Value => holder.Value;

    public async Task LoadAsync(CancellationToken ct = default)
        => holder.Value = await storage.ReadAsync(ctx.GrainId, stateName, ct).ConfigureAwait(false);

    public Task SaveAsync(CancellationToken ct = default)
        => storage.WriteAsync(ctx.GrainId, holder.Value, stateName, ct);

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await storage.ClearAsync(ctx.GrainId, stateName, ct).ConfigureAwait(false);
        holder.Value = new TState();
    }
}
