using Quark.Diagnostics.Abstractions;

namespace Quark.Performance.AstroSim;

public sealed class BenchmarkDiagnosticListener : IQuarkDiagnosticListener
{
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public void OnInvocationEnd(in InvocationEndEvent e) => Interlocked.Increment(ref _count);
}
