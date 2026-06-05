using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Quark.Transport.Abstractions;

namespace Quark.Transport.Tcp;

/// <summary>
///     Wraps a <see cref="Socket" /> (plain or TLS) as an <see cref="ITransportConnection" /> backed by
///     <see cref="System.IO.Pipelines" /> for efficient buffer management.
/// </summary>
internal sealed class TcpTransportConnection : ITransportConnection
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Pipe _inputPipe = new();
    private readonly ILogger _logger;
    private readonly Pipe _outputPipe = new();
    private readonly Socket _socket;
    private readonly Stream _stream;

    /// <summary>Creates a plain-TCP connection.</summary>
    internal TcpTransportConnection(Socket socket, ILogger logger)
        : this(socket, new NetworkStream(socket, ownsSocket: false), logger) { }

    /// <summary>Creates a TLS connection backed by the provided <paramref name="sslStream" />.</summary>
    internal TcpTransportConnection(Socket socket, SslStream sslStream, ILogger logger)
        : this(socket, (Stream)sslStream, logger) { }

    private TcpTransportConnection(Socket socket, Stream stream, ILogger logger)
    {
        _socket = socket;
        _stream = stream;
        ConnectionId = Guid.NewGuid().ToString("N");
        LocalEndPoint = socket.LocalEndPoint;
        RemoteEndPoint = socket.RemoteEndPoint;
        Transport = new DuplexPipe(_outputPipe.Reader, _inputPipe.Writer);
        _logger = logger;
    }

    /// <inheritdoc />
    public string ConnectionId { get; }

    /// <inheritdoc />
    public EndPoint? LocalEndPoint { get; }

    /// <inheritdoc />
    public EndPoint? RemoteEndPoint { get; }

    /// <inheritdoc />
    public IDuplexPipe Transport { get; }

    /// <inheritdoc />
    public Task Completion => _completion.Task;

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Task fillTask = FillInputPipeAsync(cancellationToken);
            Task drainTask = DrainOutputPipeAsync(cancellationToken);
            await Task.WhenAll(fillTask, drainTask).ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        _socket.Shutdown(SocketShutdown.Both);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _socket.Dispose();
    }

    private async Task FillInputPipeAsync(CancellationToken ct)
    {
        PipeWriter writer = _inputPipe.Writer;
        try
        {
            while (true)
            {
                Memory<byte> buffer = writer.GetMemory(4096);
                int read = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                writer.Advance(read);
                FlushResult result = await writer.FlushAsync(ct).ConfigureAwait(false);
                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TCP receive loop ended for connection {ConnectionId}.", ConnectionId);
            await writer.CompleteAsync(ex).ConfigureAwait(false);
            return;
        }

        await writer.CompleteAsync().ConfigureAwait(false);
    }

    private async Task DrainOutputPipeAsync(CancellationToken ct)
    {
        PipeReader reader = _outputPipe.Reader;
        try
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                foreach (ReadOnlyMemory<byte> segment in buffer)
                {
                    await _stream.WriteAsync(segment, ct).ConfigureAwait(false);
                }

                reader.AdvanceTo(buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TCP send loop ended for connection {ConnectionId}.", ConnectionId);
            await reader.CompleteAsync(ex).ConfigureAwait(false);
            return;
        }

        await reader.CompleteAsync().ConfigureAwait(false);
    }

    private sealed class DuplexPipe : IDuplexPipe
    {
        public DuplexPipe(PipeReader input, PipeWriter output)
        {
            Input = input;
            Output = output;
        }

        public PipeReader Input { get; }
        public PipeWriter Output { get; }
    }
}
