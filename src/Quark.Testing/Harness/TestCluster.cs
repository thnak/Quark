namespace Quark.Testing.Harness;

/// <summary>
///     An in-process Quark cluster used by tests.
///     Starts one or more <see cref="TestSilo" /> instances and a connected test client.
/// </summary>
public sealed class TestCluster : IAsyncDisposable
{
    private readonly TestClusterOptions _options;
    private readonly List<TestSilo> _silos = new();
    private TestClient? _client;
    private bool _started;

    private TestCluster(TestClusterOptions options)
    {
        _options = options;
    }

    /// <summary>All silos currently in the cluster.</summary>
    public IReadOnlyList<TestSilo> Silos => _silos;

    /// <summary>Gets the primary silo (first one started).</summary>
    public TestSilo PrimarySilo => _silos.Count > 0
        ? _silos[0]
        : throw new InvalidOperationException("TestCluster has no started silos.");

    /// <summary>
    ///     Client facade for invoking grains in this cluster.
    ///     Mirrors Orleans testing concept where TestCluster exposes client-side grain access.
    /// </summary>
    public TestClient Client => _client
                                ?? throw new InvalidOperationException("TestCluster is not started.");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Builds and starts a <see cref="TestCluster" /> with default options.</summary>
    public static async Task<TestCluster> CreateAsync(
        Action<TestClusterOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        TestClusterOptions options = new();
        configure?.Invoke(options);
        TestCluster cluster = new(options);
        await cluster.StartAsync(cancellationToken).ConfigureAwait(false);
        return cluster;
    }

    /// <summary>
    ///     Starts all configured silos.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException("TestCluster is already started.");
        }

        for (int i = 0; i < _options.InitialSilosCount; i++)
        {
            int siloPort = _options.BaseSiloPort + i;
            int gatewayPort = _options.BaseGatewayPort + i;

            TestSilo silo = new($"TestSilo-{i}", siloPort, gatewayPort, _options);
            await silo.StartAsync(cancellationToken).ConfigureAwait(false);
            _silos.Add(silo);
        }

        _client = new TestClient(PrimarySilo.Services);
        await _client.ConnectAsync().ConfigureAwait(false);

        _started = true;
    }

    /// <summary>Stops all silos and releases resources.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            await _client.CloseAsync().ConfigureAwait(false);
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }

        foreach (TestSilo silo in _silos)
        {
            await silo.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _silos.Clear();
        _started = false;
    }
}
