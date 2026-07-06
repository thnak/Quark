using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Transport.Abstractions;
using Quark.Transport.Tcp;
using Xunit;

namespace Quark.Tests.Unit.Transport;

/// <summary>
///     Verifies that <see cref="TcpTransportOptions" /> socket properties (NoDelay, KeepAlive,
///     ReceiveBufferSize, SendBufferSize) are applied to the underlying socket at both
///     connect time and accept time, and that <see cref="Quark.Transport.Abstractions.TransportOptions.IdleTimeout"/>
///     closes idle connections.
/// </summary>
public sealed class TcpSocketOptionsTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    // =====================================================================
    // ApplySocketOptions unit tests — directly exercise the helper
    // =====================================================================

    [Fact]
    public void ApplySocketOptions_NagleDisabled_SetsNoDelayTrue()
    {
        using Socket socket = CreateTcpSocket();
        TcpTransport.ApplySocketOptions(socket, new TcpTransportOptions { EnableNagle = false });
        Assert.True(socket.NoDelay);
    }

    [Fact]
    public void ApplySocketOptions_NagleEnabled_SetsNoDelayFalse()
    {
        using Socket socket = CreateTcpSocket();
        TcpTransport.ApplySocketOptions(socket, new TcpTransportOptions { EnableNagle = true });
        Assert.False(socket.NoDelay);
    }

    [Fact]
    public void ApplySocketOptions_KeepAliveTrue_EnablesKeepAlive()
    {
        using Socket socket = CreateTcpSocket();
        TcpTransport.ApplySocketOptions(socket, new TcpTransportOptions { KeepAlive = true });
        int value = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!;
        Assert.NotEqual(0, value);
    }

    [Fact]
    public void ApplySocketOptions_KeepAliveFalse_DisablesKeepAlive()
    {
        using Socket socket = CreateTcpSocket();
        TcpTransport.ApplySocketOptions(socket, new TcpTransportOptions { KeepAlive = false });
        int value = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!;
        Assert.Equal(0, value);
    }

    [Fact]
    public void ApplySocketOptions_BufferSizes_AreAppliedToSocket()
    {
        using Socket socket = CreateTcpSocket();
        TcpTransport.ApplySocketOptions(socket, new TcpTransportOptions
        {
            ReceiveBufferSize = 4096,
            SendBufferSize = 8192,
        });
        // The OS may adjust buffer sizes (Linux doubles SO_RCVBUF/SO_SNDBUF).
        // Verify the configured values were applied and the socket reflects at least the requested size.
        Assert.True(socket.ReceiveBufferSize >= 4096,
            $"Expected ReceiveBufferSize >= 4096 but got {socket.ReceiveBufferSize}");
        Assert.True(socket.SendBufferSize >= 8192,
            $"Expected SendBufferSize >= 8192 but got {socket.SendBufferSize}");
    }

    // =====================================================================
    // Integration: socket options visible on connected/accepted sockets
    // =====================================================================

    [Fact]
    public async Task ConnectAsync_AppliesNoDelay_OnClientSocket()
    {
        var options = new TcpTransportOptions { EnableNagle = false, IdleTimeout = Timeout.InfiniteTimeSpan };
        await using Pair pair = await Pair.CreateAsync(options);
        Assert.True(GetSocket(pair.ClientConnection).NoDelay);
    }

    [Fact]
    public async Task ConnectAsync_NagleEnabled_SetsNoDelayFalseOnClientSocket()
    {
        var options = new TcpTransportOptions { EnableNagle = true, IdleTimeout = Timeout.InfiniteTimeSpan };
        await using Pair pair = await Pair.CreateAsync(options);
        Assert.False(GetSocket(pair.ClientConnection).NoDelay);
    }

    [Fact]
    public async Task AcceptAsync_AppliesNoDelay_OnServerSocket()
    {
        var options = new TcpTransportOptions { EnableNagle = false, IdleTimeout = Timeout.InfiniteTimeSpan };
        await using Pair pair = await Pair.CreateAsync(options);
        Assert.True(GetSocket(pair.ServerConnection).NoDelay);
    }

    [Fact]
    public async Task ConnectAsync_AppliesKeepAlive_OnClientSocket()
    {
        var options = new TcpTransportOptions { KeepAlive = true, IdleTimeout = Timeout.InfiniteTimeSpan };
        await using Pair pair = await Pair.CreateAsync(options);
        int value = (int)GetSocket(pair.ClientConnection)
            .GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!;
        Assert.NotEqual(0, value);
    }

    [Fact]
    public async Task AcceptAsync_AppliesKeepAlive_OnServerSocket()
    {
        var options = new TcpTransportOptions { KeepAlive = true, IdleTimeout = Timeout.InfiniteTimeSpan };
        await using Pair pair = await Pair.CreateAsync(options);
        int value = (int)GetSocket(pair.ServerConnection)
            .GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!;
        Assert.NotEqual(0, value);
    }

    [Fact]
    public async Task ConnectAsync_AppliesBufferSizes_OnClientSocket()
    {
        var options = new TcpTransportOptions
        {
            ReceiveBufferSize = 4096,
            SendBufferSize = 8192,
            IdleTimeout = Timeout.InfiniteTimeSpan,
        };
        await using Pair pair = await Pair.CreateAsync(options);
        Socket socket = GetSocket(pair.ClientConnection);
        Assert.True(socket.ReceiveBufferSize >= 4096,
            $"ReceiveBufferSize expected >= 4096, got {socket.ReceiveBufferSize}");
        Assert.True(socket.SendBufferSize >= 8192,
            $"SendBufferSize expected >= 8192, got {socket.SendBufferSize}");
    }

    // =====================================================================
    // Idle timeout
    // =====================================================================

    [Fact]
    public async Task IdleTimeout_ClosesConnectionAfterInactivity()
    {
        // Use a short idle timeout so the test completes quickly.
        // The check period is max(50ms, IdleTimeout/2) = max(50ms, 100ms) = 100ms.
        // So the connection should close within roughly 300ms of becoming idle.
        var options = new TcpTransportOptions { IdleTimeout = TimeSpan.FromMilliseconds(200) };

        var cts = new CancellationTokenSource(TestTimeout);
        await using Pair pair = await Pair.CreateAsync(options, startPumps: true);

        // Wait for the server side to complete (idle timeout closes it).
        await pair.ServerCompletion.WaitAsync(cts.Token);
        Assert.True(pair.ServerCompletion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task IdleTimeout_DisabledWhenSetToInfinite_ConnectionStaysOpen()
    {
        var options = new TcpTransportOptions { IdleTimeout = Timeout.InfiniteTimeSpan };
        await using Pair pair = await Pair.CreateAsync(options, startPumps: true);

        // Exchange data to prove the connection is alive
        byte[] payload = [1, 2, 3];
        await pair.ClientConnection.Transport.Output.WriteAsync(payload);
        byte[] received = new byte[3];
        using var cts = new CancellationTokenSource(TestTimeout);
        ReadResult read = await pair.ServerConnection.Transport.Input.ReadAsync(cts.Token);
        read.Buffer.CopyTo(received.AsSpan());
        pair.ServerConnection.Transport.Input.AdvanceTo(read.Buffer.End);
        Assert.Equal(payload, received);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static Socket CreateTcpSocket()
        => new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_socket")]
    private static extern ref Socket GetSocketField(TcpTransportConnection connection);

    private static Socket GetSocket(TcpTransportConnection connection) => GetSocketField(connection);

    private sealed class Pair : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly ITransportListener _listener;
        private readonly Task? _clientExec;
        private readonly Task? _serverExec;

        private Pair(
            TcpTransportConnection client,
            TcpTransportConnection server,
            ITransportListener listener,
            CancellationTokenSource cts,
            Task? clientExec,
            Task? serverExec)
        {
            ClientConnection = client;
            ServerConnection = server;
            _listener = listener;
            _cts = cts;
            _clientExec = clientExec;
            _serverExec = serverExec;
        }

        public TcpTransportConnection ClientConnection { get; }
        public TcpTransportConnection ServerConnection { get; }

        public Task ServerCompletion => ServerConnection.Completion;

        public static async Task<Pair> CreateAsync(TcpTransportOptions options, bool startPumps = false)
        {
            var transport = new TcpTransport(options, NullLogger<TcpTransport>.Instance);
            ITransportListener listener = transport.CreateListener(new IPEndPoint(IPAddress.Loopback, 0));
            await listener.BindAsync();

            ValueTask<ITransportConnection?> acceptTask = listener.AcceptAsync();
            ITransportConnection rawClient = await transport.ConnectAsync(listener.LocalEndPoint);
            ITransportConnection rawServer = (await acceptTask)!;

            var client = (TcpTransportConnection)rawClient;
            var server = (TcpTransportConnection)rawServer;

            var cts = new CancellationTokenSource();
            Task? clientExec = null;
            Task? serverExec = null;

            if (startPumps)
            {
                clientExec = client.ExecuteAsync(cts.Token);
                serverExec = server.ExecuteAsync(cts.Token);
            }

            return new Pair(client, server, listener, cts, clientExec, serverExec);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await Swallow(ClientConnection.DisposeAsync().AsTask());
            await Swallow(ServerConnection.DisposeAsync().AsTask());
            await _listener.StopAsync();
            await _listener.DisposeAsync();
            if (_clientExec is not null) await Swallow(_clientExec);
            if (_serverExec is not null) await Swallow(_serverExec);
            _cts.Dispose();
        }

        private static async Task Swallow(Task t)
        {
            try { await t; } catch { }
        }
    }
}
