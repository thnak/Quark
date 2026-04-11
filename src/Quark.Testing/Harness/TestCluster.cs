using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Testing.Harness;

namespace Quark.Testing;

/// <summary>
/// An in-process Quark cluster used by tests.
/// Starts one or more <see cref="TestSilo"/> instances and a connected test client.
/// </summary>
public sealed class TestCluster : IAsyncDisposable
{
    private readonly TestClusterOptions _options;
    private readonly List<TestSilo> _silos = new();
    private bool _started;

    private TestCluster(TestClusterOptions options)
    {
        _options = options;
    }

    /// <summary>Builds and starts a <see cref="TestCluster"/> with default options.</summary>
    public static Task<TestCluster> CreateAsync(
        Action<TestClusterOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        TestClusterOptions options = new();
        configure?.Invoke(options);
        TestCluster cluster = new(options);
        return cluster.StartAsync(cancellationToken).ContinueWith(_ => cluster, cancellationToken);
    }

    /// <summary>All silos currently in the cluster.</summary>
    public IReadOnlyList<TestSilo> Silos => _silos;

    /// <summary>Gets the primary silo (first one started).</summary>
    public TestSilo PrimarySilo => _silos[0];

    /// <summary>
    /// Starts all configured silos.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            throw new InvalidOperationException("TestCluster is already started.");

        for (int i = 0; i < _options.InitialSilosCount; i++)
        {
            int siloPort = _options.BaseSiloPort + i;
            int gatewayPort = _options.BaseGatewayPort + i;

            TestSilo silo = new($"TestSilo-{i}", siloPort, gatewayPort, _options);
            await silo.StartAsync(cancellationToken).ConfigureAwait(false);
            _silos.Add(silo);
        }

        _started = true;
    }

    /// <summary>Stops all silos and releases resources.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        foreach (TestSilo silo in _silos)
            await silo.StopAsync(cancellationToken).ConfigureAwait(false);
        _silos.Clear();
        _started = false;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
