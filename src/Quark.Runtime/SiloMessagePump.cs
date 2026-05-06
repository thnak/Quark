using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Long-running pump which accepts framed messages from transport connections and dispatches them.
/// </summary>
public sealed class SiloMessagePump : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly MessageSerializer _serializer;
    private readonly IMessageDispatcher _dispatcher;
    private readonly SiloRuntimeOptions _options;
    private readonly ILogger<SiloMessagePump> _logger;
    private readonly List<Task> _connectionTasks = new();
    private CancellationTokenSource? _cts;
    private ITransportListener? _listener;
    private Task? _acceptLoop;

    /// <summary>Initializes the message pump.</summary>
    public SiloMessagePump(
        IServiceProvider services,
        MessageSerializer serializer,
        IMessageDispatcher dispatcher,
        IOptions<SiloRuntimeOptions> options,
        ILogger<SiloMessagePump> logger)
    {
        _services = services;
        _serializer = serializer;
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Starts the pump if a transport has been registered.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_acceptLoop is not null)
            return;

        ITransport? transport = _services.GetService(typeof(ITransport)) as ITransport;
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

    /// <summary>Processes a single accepted connection until it closes.</summary>
    public async Task ProcessConnectionAsync(ITransportConnection connection, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task executeTask = connection.ExecuteAsync(linkedCts.Token);

        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                MessageEnvelope? envelope = await _serializer.ReadAsync(connection.Transport.Input, linkedCts.Token)
                    .ConfigureAwait(false);
                if (envelope is null)
                    break;

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

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task AcceptLoopAsync(ITransportListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ITransportConnection? connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
                break;

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

    private static EndPoint ResolveEndPoint(SiloAddress address)
    {
        if (IPAddress.TryParse(address.Host, out IPAddress? ipAddress))
            return new IPEndPoint(ipAddress, address.Port);

        IPAddress resolved = Dns.GetHostAddresses(address.Host)
                                 .FirstOrDefault(static ip => ip.AddressFamily == AddressFamily.InterNetwork)
                             ?? IPAddress.Loopback;

        return new IPEndPoint(resolved, address.Port);
    }
}