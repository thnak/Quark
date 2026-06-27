using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Long-running pump which accepts framed messages from transport connections and dispatches them.
/// </summary>
public sealed class SiloMessagePump : IAsyncDisposable
{
    private readonly List<Task> _connectionTasks = new();
    private readonly IMessageDispatcher _dispatcher;
    private readonly ILogger<SiloMessagePump> _logger;
    private readonly SiloRuntimeOptions _options;
    private readonly MessageSerializer _serializer;
    private readonly IServiceProvider _services;
    private readonly int _maxConnections;
    private Task? _acceptLoop;
    private CancellationTokenSource? _cts;
    private ITransportListener? _listener;

    /// <summary>Initializes the message pump.</summary>
    public SiloMessagePump(
        IServiceProvider services,
        MessageSerializer serializer,
        IMessageDispatcher dispatcher,
        IOptions<SiloRuntimeOptions> options,
        ILogger<SiloMessagePump> logger,
        TransportOptions? transportOptions = null)
    {
        _services = services;
        _serializer = serializer;
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
        _maxConnections = transportOptions?.MaxConnections ?? 0;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Starts the pump if a transport has been registered.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_acceptLoop is not null)
        {
            return;
        }

        var transport = _services.GetService(typeof(ITransport)) as ITransport;
        if (transport is null)
        {
            _logger.LogDebug("No transport registered; silo message pump remains idle.");
            return;
        }

        EndPoint endPoint = ResolveEndPoint(_options.SiloAddress);
        _listener = transport.CreateListener(endPoint);
        await _listener.BindAsync(cancellationToken).ConfigureAwait(false);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);
    }

    /// <summary>Stops the accept loop and waits for active connections to finish.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
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

    /// <summary>Processes a single accepted connection until it closes.</summary>
    private async Task ProcessConnectionAsync(ITransportConnection connection,
        CancellationToken cancellationToken = default)
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
            await linkedCts.CancelAsync();
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

            // Shed inbound connections beyond the configured concurrency cap so a flood of opens
            // cannot spawn an unbounded number of per-connection processing loops. Only the accept
            // loop adds to _connectionTasks, so the count never under-reports the live total here.
            if (_maxConnections > 0)
            {
                int active;
                lock (_connectionTasks)
                {
                    active = _connectionTasks.Count;
                }

                if (active >= _maxConnections)
                {
                    _logger.LogWarning(
                        "Rejecting inbound connection {ConnectionId}: concurrent connection limit ({Max}) reached.",
                        connection.ConnectionId, _maxConnections);
                    await CloseRejectedAsync(connection).ConfigureAwait(false);
                    continue;
                }
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
                    _logger.LogWarning(t.Exception, "Message pump connection loop failed.");
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task CloseRejectedAsync(ITransportConnection connection)
    {
        try
        {
            await connection.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error closing rejected connection {ConnectionId}.", connection.ConnectionId);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
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
