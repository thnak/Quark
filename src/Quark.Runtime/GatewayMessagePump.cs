using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Long-running pump that accepts framed messages from external TCP client connections
///     and dispatches them through <see cref="IMessageDispatcher" />.
///     Listens on <see cref="SiloRuntimeOptions.GatewayAddress" /> (port 30000 by default).
///     Registered only when <c>UseLocalhostClustering()</c> is called.
/// </summary>
public sealed class GatewayMessagePump : IHostedService, IAsyncDisposable
{
    private readonly List<Task> _connectionTasks = new();
    private readonly IMessageDispatcher _dispatcher;
    private readonly ILogger<GatewayMessagePump> _logger;
    private readonly SiloRuntimeOptions _options;
    private readonly MessageSerializer _serializer;
    private readonly IServiceProvider _services;
    private Task? _acceptLoop;
    private CancellationTokenSource? _cts;
    private ITransportListener? _listener;

    public GatewayMessagePump(
        IServiceProvider services,
        MessageSerializer serializer,
        IMessageDispatcher dispatcher,
        IOptions<SiloRuntimeOptions> options,
        ILogger<GatewayMessagePump> logger)
    {
        _services = services;
        _serializer = serializer;
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_acceptLoop is not null)
        {
            return;
        }

        var transport = _services.GetService(typeof(ITransport)) as ITransport;
        if (transport is null)
        {
            _logger.LogDebug("No transport registered; gateway message pump remains idle.");
            return;
        }

        EndPoint endPoint = ResolveEndPoint(_options.GatewayAddress);
        _listener = transport.CreateListener(endPoint);
        await _listener.BindAsync(cancellationToken).ConfigureAwait(false);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);

        _logger.LogInformation("Gateway message pump listening on {Address}.", _options.GatewayAddress);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }

        if (_listener is not null)
        {
            await _listener.StopAsync(cancellationToken).ConfigureAwait(false);
            await _listener.DisposeAsync().ConfigureAwait(false);
            _listener = null;
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
            {
            }

            _acceptLoop = null;
        }

        Task[] remaining;
        lock (_connectionTasks)
        {
            remaining = _connectionTasks.ToArray();
        }

        await Task.WhenAll(remaining).ConfigureAwait(false);
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ProcessConnectionAsync(ITransportConnection connection,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task executeTask = connection.ExecuteAsync(linkedCts.Token);

        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                MessageEnvelope? envelope = await _serializer.ReadAsync(connection.Transport.Input, linkedCts.Token)
                    .ConfigureAwait(false);
                if (envelope is null)
                {
                    break;
                }

                MessageEnvelope? response = await _dispatcher.DispatchAsync(envelope, linkedCts.Token)
                    .ConfigureAwait(false);
                if (response is not null)
                {
                    await _serializer.WriteAsync(connection.Transport.Output, response, linkedCts.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            linkedCts.Cancel();
            await connection.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await executeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
            }

            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task AcceptLoopAsync(ITransportListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ITransportConnection? connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                break;
            }

            Task task = ProcessConnectionAsync(connection, cancellationToken);
            lock (_connectionTasks)
            {
                _connectionTasks.Add(task);
            }

            _ = task.ContinueWith(t =>
            {
                lock (_connectionTasks)
                {
                    _connectionTasks.Remove(t);
                }

                if (t.Exception is not null)
                {
                    _logger.LogWarning(t.Exception, "Gateway connection loop failed.");
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static EndPoint ResolveEndPoint(SiloAddress address)
    {
        if (IPAddress.TryParse(address.Host, out IPAddress? ipAddress))
        {
            return new IPEndPoint(ipAddress, address.Port);
        }

        IPAddress resolved = Dns.GetHostAddresses(address.Host)
                                 .FirstOrDefault(static ip => ip.AddressFamily == AddressFamily.InterNetwork)
                             ?? IPAddress.Loopback;

        return new IPEndPoint(resolved, address.Port);
    }
}
