using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Transport.Abstractions;
using Quark.Transport.Tcp;
using Xunit;

namespace Quark.Tests.Unit.Transport;

/// <summary>
///     Resilience coverage for <see cref="TcpTransportConnection"/>'s pipe pump (issue #34):
///     the receive loop (<c>FillInputPipeAsync</c>) and send loop (<c>DrainOutputPipeAsync</c>) over
///     <see cref="System.IO.Pipelines"/>. Drives raw bytes through a real loopback
///     <see cref="TcpTransportListener"/> + <see cref="TcpTransport"/> pair, exercising multi-segment
///     reassembly, partial-frame delivery, EOF on peer close, abrupt dispose, and round-trips —
///     the conditions the happy-path TLS tests never produce. In-process; no Testcontainers.
/// </summary>
public sealed class TcpTransportConnectionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // =====================================================================
    // Reassembly
    // =====================================================================

    [Fact]
    public async Task LargePayload_SpanningManySegments_IsReassembledByteExact()
    {
        await using Pair pair = await Pair.CreateAsync();
        // 256 KiB — far larger than the 4096-byte GetMemory hint, so it crosses many
        // receive segments and several DrainOutputPipe write loops.
        // Read and write concurrently: with explicit small TCP buffer sizes the server's
        // receive buffer fills quickly, causing flow-control if nobody drains the server
        // input pipe in parallel with the client write.
        byte[] payload = Pattern(256 * 1024);

        Task<byte[]> readTask = ReadExactlyAsync(pair.Server.Transport.Input, payload.Length);
        await pair.Client.Transport.Output.WriteAsync(payload);
        byte[] received = await readTask;

        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task PartialFrame_DeliveredInSmallChunksWithDelays_IsReassembledInOrder()
    {
        await using Pair pair = await Pair.CreateAsync();
        byte[][] chunks =
        [
            Pattern(7, seed: 1),
            Pattern(13, seed: 2),
            Pattern(1, seed: 3),
            Pattern(5000, seed: 4),
            Pattern(64, seed: 5),
        ];
        byte[] expected = chunks.SelectMany(c => c).ToArray();

        foreach (byte[] chunk in chunks)
        {
            await pair.Client.Transport.Output.WriteAsync(chunk);
            await Task.Delay(5); // force separate ReadAsync deliveries on the receiver
        }

        byte[] received = await ReadExactlyAsync(pair.Server.Transport.Input, expected.Length);
        Assert.Equal(expected, received);
    }

    [Fact]
    public async Task BackToBackBatches_Bidirectional_RoundTripInOrder()
    {
        await using Pair pair = await Pair.CreateAsync();
        const int batches = 50;
        byte[] c2s = Enumerable.Range(0, batches).SelectMany(i => Pattern(200, seed: i)).ToArray();
        byte[] s2c = Enumerable.Range(0, batches).SelectMany(i => Pattern(150, seed: 1000 + i)).ToArray();

        Task<byte[]> serverReads = ReadExactlyAsync(pair.Server.Transport.Input, c2s.Length);
        Task<byte[]> clientReads = ReadExactlyAsync(pair.Client.Transport.Input, s2c.Length);

        for (int i = 0; i < batches; i++)
        {
            await pair.Client.Transport.Output.WriteAsync(Pattern(200, seed: i));
            await pair.Server.Transport.Output.WriteAsync(Pattern(150, seed: 1000 + i));
        }

        Assert.Equal(c2s, await serverReads);
        Assert.Equal(s2c, await clientReads);
    }

    // =====================================================================
    // EOF / connection drop
    // =====================================================================

    [Fact]
    public async Task GracefulPeerClose_AfterData_ReceiverDrainsThenObservesCleanCompletion()
    {
        await using Pair pair = await Pair.CreateAsync();
        byte[] payload = Pattern(32);

        await pair.Client.Transport.Output.WriteAsync(payload);
        Assert.Equal(payload, await ReadExactlyAsync(pair.Server.Transport.Input, payload.Length));

        // Graceful shutdown sends FIN; the receiver sees a clean EOF (read == 0).
        await pair.Client.CloseAsync();

        ReadResult tail = await ReadToCompletionAsync(pair.Server.Transport.Input);
        Assert.True(tail.IsCompleted);
        Assert.Equal(0, tail.Buffer.Length);
    }

    [Fact]
    public async Task PeerCloses_AfterOutputCompleted_ConnectionCompletionResolves()
    {
        await using Pair pair = await Pair.CreateAsync();

        // End the send loop cleanly, then drop the peer to end the receive loop.
        await pair.Server.Transport.Output.CompleteAsync();
        await pair.Client.DisposeAsync();

        // Both pumps finished → Completion resolves successfully (no hang).
        await pair.Server.Completion.WaitAsync(Timeout);
        Assert.True(pair.Server.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AbruptDispose_WhilePumpsRunning_DoesNotThrow_AndPeerTerminatesWithoutHanging()
    {
        await using Pair pair = await Pair.CreateAsync();

        await pair.Client.Transport.Output.WriteAsync(Pattern(16));

        // Abruptly tear down the server side while its loops are active. Disposing the socket
        // without Shutdown sends an RST, so the peer's receive pump fails with a socket reset;
        // the SUT completes the input pipe with that exception (issue #34's "abrupt socket
        // error → pipe completed with the exception" path). DisposeAsync itself must not throw.
        await pair.Server.DisposeAsync();

        // The peer must terminate — either a reset error surfaced through the pipe or a clean
        // EOF, depending on OS timing — but never hang.
        await AssertReaderTerminatesAsync(pair.Client.Transport.Input);
    }

    [Fact]
    public async Task CloseAsync_ShutsDownSocket_AndReceiverObservesCompletion()
    {
        await using Pair pair = await Pair.CreateAsync();

        await pair.Client.CloseAsync();

        ReadResult tail = await ReadToCompletionAsync(pair.Server.Transport.Input);
        Assert.True(tail.IsCompleted);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static byte[] Pattern(int length, int seed = 0)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = (byte)((i + seed * 31) % 251);
        return data;
    }

    private static async Task<byte[]> ReadExactlyAsync(PipeReader reader, int count)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var result = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            ReadResult read = await reader.ReadAsync(cts.Token);
            ReadOnlySequence<byte> buffer = read.Buffer;
            long take = Math.Min(buffer.Length, count - offset);
            buffer.Slice(0, take).CopyTo(result.AsSpan(offset));
            offset += (int)take;
            reader.AdvanceTo(buffer.GetPosition(take), buffer.End);
            if (offset < count && read.IsCompleted)
                throw new InvalidOperationException(
                    $"stream completed after {offset} of {count} bytes");
        }

        return result;
    }

    /// <summary>
    ///     Asserts the reader reaches a terminal state without hanging — either a clean
    ///     <see cref="ReadResult.IsCompleted"/> (FIN) or the receive pump's faulting exception
    ///     rethrown by the pipe (RST). A hang manifests as the timeout cancelling the read.
    /// </summary>
    private static async Task AssertReaderTerminatesAsync(PipeReader reader)
    {
        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            while (true)
            {
                ReadResult read = await reader.ReadAsync(cts.Token);
                if (read.IsCompleted)
                {
                    reader.AdvanceTo(read.Buffer.End);
                    return; // clean EOF
                }

                reader.AdvanceTo(read.Buffer.Start, read.Buffer.End);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return; // reset/error propagated through the completed pipe — also terminal
        }
    }

    private static async Task<ReadResult> ReadToCompletionAsync(PipeReader reader)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (true)
        {
            ReadResult read = await reader.ReadAsync(cts.Token);
            if (read.IsCompleted)
            {
                // Leave the buffer consumed so the assertion sees the terminal state.
                reader.AdvanceTo(read.Buffer.End);
                return read;
            }

            reader.AdvanceTo(read.Buffer.Start, read.Buffer.End);
        }
    }

    /// <summary>A connected loopback pair with both pump loops running.</summary>
    private sealed class Pair : IAsyncDisposable
    {
        private readonly Task _clientExec;
        private readonly CancellationTokenSource _cts;
        private readonly ITransportListener _listener;
        private readonly Task _serverExec;

        private Pair(
            ITransportConnection client,
            ITransportConnection server,
            Task clientExec,
            Task serverExec,
            ITransportListener listener,
            CancellationTokenSource cts)
        {
            Client = client;
            Server = server;
            _clientExec = clientExec;
            _serverExec = serverExec;
            _listener = listener;
            _cts = cts;
        }

        public ITransportConnection Client { get; }
        public ITransportConnection Server { get; }

        public static async Task<Pair> CreateAsync()
        {
            // Disable idle timeout so it doesn't interfere with data-transfer tests.
            // Use explicit large buffers so TCP flow control doesn't stall under stress.
            var options = new TcpTransportOptions
            {
                IdleTimeout = System.Threading.Timeout.InfiniteTimeSpan,
                ReceiveBufferSize = 512 * 1024,
                SendBufferSize = 512 * 1024,
            };
            var serverTransport = new TcpTransport(options, NullLogger<TcpTransport>.Instance);
            var clientTransport = new TcpTransport(options, NullLogger<TcpTransport>.Instance);

            ITransportListener listener = serverTransport.CreateListener(
                new IPEndPoint(IPAddress.Loopback, 0));
            await listener.BindAsync();

            ValueTask<ITransportConnection?> acceptTask = listener.AcceptAsync();
            ITransportConnection client = await clientTransport.ConnectAsync(listener.LocalEndPoint);
            ITransportConnection server = (await acceptTask)!;

            // Fire-and-forget pump loops, mirroring SiloMessagePump/TcpGatewayConnection.
            // They are passed a cancellation token so teardown can unblock the pipe reads
            // (disposing the socket alone does not unblock a pending output-pipe ReadAsync).
            var cts = new CancellationTokenSource();
            Task clientExec = client.ExecuteAsync(cts.Token);
            Task serverExec = server.ExecuteAsync(cts.Token);

            return new Pair(client, server, clientExec, serverExec, listener, cts);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await SafeAsync(Client.DisposeAsync().AsTask());
            await SafeAsync(Server.DisposeAsync().AsTask());
            await _listener.StopAsync();
            await _listener.DisposeAsync();
            // Observe the pump tasks so faults raised by teardown don't surface as
            // unobserved task exceptions.
            await SafeAsync(_clientExec);
            await SafeAsync(_serverExec);
            _cts.Dispose();
        }

        private static async Task SafeAsync(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
                // Teardown races (disposed socket/stream) are expected.
            }
        }
    }
}
