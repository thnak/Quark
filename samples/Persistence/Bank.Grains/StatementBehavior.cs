using Bank.GrainInterfaces;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Runtime;

namespace Bank.Grains;

/// <summary>
///     Pattern 5 — <b>Managed activation memory</b> (<see cref="IManagedActivationMemory{T}" />).
///     <para>
///         A resource that needs <b>async</b> initialization but must not survive deactivation.
///         <c>Init</c> configures a factory (no DI, no params) that runs <b>lazily</b> on the first
///         <c>GetAsync</c>; the value is then cached for the activation's lifetime. <c>Destroy</c>
///         runs after <c>OnDeactivateAsync</c> — the place to flush/close the resource.
///     </para>
///     <para>
///         Call <c>close</c> (which deactivates this grain) and watch the <b>server console</b>:
///         the <c>Destroy</c> callback flushes the buffered statement, demonstrating deterministic
///         cleanup on deactivation.
///     </para>
/// </summary>
public sealed class StatementBehavior : IGrainBehavior, IStatementGrain
{
    private readonly IManagedActivationMemory<StatementBuffer> _buffer;
    private readonly IActivationShellAccessor _shell;
    private readonly ICallContext _ctx;

    public StatementBehavior(
        IManagedActivationMemory<StatementBuffer> buffer,
        IActivationShellAccessor shell,
        ICallContext ctx)
    {
        _shell = shell;
        _ctx = ctx;
        string key = ctx.GrainId.Key.ToString();
        _buffer = buffer
            .Init(async () =>
            {
                await Task.Yield(); // stand-in for an expensive async open
                return new StatementBuffer { InitCount = 1 };
            })
            .Destroy(b =>
            {
                // Runs after OnDeactivateAsync — flush the buffered statement.
                Console.WriteLine($"[statement:{key}] flushing {b.Lines.Count} line(s) on deactivation.");
                return ValueTask.CompletedTask;
            });
    }

    public async Task AddLineAsync(string line)
    {
        StatementBuffer buf = await _buffer.GetAsync();
        buf.Lines.Add(line);
    }

    public async Task<string> RenderAsync()
    {
        StatementBuffer buf = await _buffer.GetAsync();
        return buf.Lines.Count == 0 ? "(empty statement)" : string.Join(Environment.NewLine, buf.Lines);
    }

    public async Task<int> GetInitCountAsync()
    {
        StatementBuffer buf = await _buffer.GetAsync();
        return buf.InitCount;
    }

    public Task CloseAsync()
    {
        // Triggers deactivation → OnDeactivateAsync → the managed Destroy callback flushes.
        _shell.Shell.Deactivate(DeactivationReason.ApplicationRequested);
        return Task.CompletedTask;
    }
}
