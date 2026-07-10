using Microsoft.Extensions.DependencyInjection;
using Quark.Diagnostics;
using Quark.Diagnostics.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Diagnostics;

/// <summary>
///     Regression coverage for a circular-DI bug in <see cref="DiagnosticsServiceCollectionExtensions" />:
///     <c>CompositeDiagnosticListener</c> used to depend on <c>IEnumerable&lt;IQuarkDiagnosticListener&gt;</c>
///     directly, which included its own self-referencing factory registration -- resolving
///     <see cref="IQuarkDiagnosticListener" /> recursively reconstructed the composite forever
///     (reproduced as a real silo-startup hang while wiring diagnostics into the Realm sample, and
///     independently in <c>Quark.Performance</c>'s AstroSim/PingPong/Fairness benchmark runners, per
///     <c>docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md</c> section 5).
/// </summary>
public sealed class DiagnosticsCompositeRegistrationTests
{
    [Fact]
    public void AddQuarkDiagnostics_SingleListener_ResolvesWithoutRecursion()
    {
        var services = new ServiceCollection();
        var listener = new RecordingListener();
        services.AddQuarkDiagnostics(listener);

        using ServiceProvider sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IQuarkDiagnosticListener>();

        var e = new ObserverInvokedEvent(default, 0, true, null);
        resolved.OnObserverInvoked(in e);

        Assert.Equal(1, listener.InvokeCount);
    }

    [Fact]
    public void AddQuarkDiagnostics_MultipleListeners_AllReceiveEvents()
    {
        var services = new ServiceCollection();
        var first = new RecordingListener();
        var second = new RecordingListener();
        services.AddQuarkDiagnostics(first);
        services.AddQuarkDiagnostics(second);

        using ServiceProvider sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IQuarkDiagnosticListener>();

        var e = new ObserverInvokedEvent(default, 0, true, null);
        resolved.OnObserverInvoked(in e);

        Assert.Equal(1, first.InvokeCount);
        Assert.Equal(1, second.InvokeCount);
    }

    [Fact]
    public void AddQuarkDiagnostics_GenericOverload_ResolvesWithoutRecursion()
    {
        var services = new ServiceCollection();
        services.AddQuarkDiagnostics<RecordingListener>();

        using ServiceProvider sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IQuarkDiagnosticListener>();

        var e = new ObserverInvokedEvent(default, 0, true, null);
        resolved.OnObserverInvoked(in e);

        var registered = sp.GetRequiredService<RecordingListener>();
        Assert.Equal(1, registered.InvokeCount);
    }

    private sealed class RecordingListener : IQuarkDiagnosticListener
    {
        public int InvokeCount { get; private set; }
        public void OnObserverInvoked(in ObserverInvokedEvent e) => InvokeCount++;
    }
}
