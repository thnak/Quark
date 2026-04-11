using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions;
using Quark.Serialization.Abstractions;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Encodes and decodes wire-level <see cref="MessageEnvelope"/> instances.
/// </summary>
public sealed class MessageSerializer
{
    /// <summary>Serializes <paramref name="envelope"/> into a byte array.</summary>
    public byte[] Serialize(MessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);

        writer.WriteInt64(envelope.CorrelationId);
        writer.WriteByte((byte)envelope.MessageType);

        IReadOnlyDictionary<string, string> headers = envelope.Headers?.All
            ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
        writer.WriteVarUInt32((uint)headers.Count);
        foreach ((string key, string value) in headers)
        {
            writer.WriteString(key);
            writer.WriteString(value);
        }

        writer.WriteBytes(envelope.Payload.Span);
        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Deserializes a <see cref="MessageEnvelope"/> from <paramref name="buffer"/>.</summary>
    public MessageEnvelope Deserialize(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        long correlationId = reader.ReadInt64();
        MessageType messageType = (MessageType)reader.ReadByte();

        uint headerCount = reader.ReadVarUInt32();
        MessageHeaders? headers = headerCount > 0 ? new MessageHeaders() : null;
        for (uint i = 0; i < headerCount; i++)
        {
            string key = reader.ReadString();
            string value = reader.ReadString();
            headers!.Set(key, value);
        }

        byte[] payload = reader.ReadBytes();
        return new MessageEnvelope
        {
            CorrelationId = correlationId,
            MessageType = messageType,
            Headers = headers,
            Payload = payload
        };
    }

    /// <summary>Writes a length-prefixed envelope to a pipe.</summary>
    public async ValueTask WriteAsync(PipeWriter writer, MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        byte[] bytes = Serialize(envelope);
        Span<byte> prefix = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        writer.Advance(sizeof(int));

        bytes.CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the next length-prefixed envelope from a pipe, or <c>null</c> on EOF.</summary>
    public async ValueTask<MessageEnvelope?> ReadAsync(PipeReader reader, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (TryReadEnvelope(ref buffer, out MessageEnvelope? envelope))
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                return envelope;
            }

            if (result.IsCompleted)
            {
                reader.AdvanceTo(buffer.End);
                return buffer.Length == 0 ? null : throw new EndOfStreamException("Incomplete message envelope frame.");
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private bool TryReadEnvelope(ref ReadOnlySequence<byte> buffer, out MessageEnvelope? envelope)
    {
        envelope = null;

        if (buffer.Length < sizeof(int))
            return false;

        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        buffer.Slice(0, sizeof(int)).CopyTo(lengthBytes);
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        if (buffer.Length < sizeof(int) + payloadLength)
            return false;

        ReadOnlySequence<byte> payload = buffer.Slice(sizeof(int), payloadLength);
        envelope = Deserialize(payload.ToArray());
        buffer = buffer.Slice(sizeof(int) + payloadLength);
        return true;
    }
}

/// <summary>
/// Payload contract for a network-routed grain call.
/// </summary>
public sealed record GrainInvocationRequest(GrainId GrainId, uint MethodId, object?[]? Arguments);

/// <summary>
/// Payload contract for a network-routed grain call response.
/// </summary>
public sealed record GrainInvocationResponse(bool Success, object? Result, string? Error);

/// <summary>
/// Serializes request/response payloads for transport-routed grain invocation.
/// </summary>
public sealed class GrainMessageSerializer
{
    private enum ValueKind : byte
    {
        Null = 0,
        Boolean = 1,
        Int32 = 2,
        UInt32 = 3,
        Int64 = 4,
        UInt64 = 5,
        String = 6,
        Guid = 7,
        ByteArray = 8,
        Double = 9,
        Single = 10,
        Decimal = 11,
    }

    /// <summary>Serializes a grain invocation request.</summary>
    public byte[] SerializeRequest(GrainInvocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);
        writer.WriteString(request.GrainId.Type.Value);
        writer.WriteString(request.GrainId.Key);
        writer.WriteVarUInt32(request.MethodId);

        object?[] args = request.Arguments ?? [];
        writer.WriteVarUInt32((uint)args.Length);
        foreach (object? arg in args)
            WriteValue(writer, arg);

        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Deserializes a grain invocation request.</summary>
    public GrainInvocationRequest DeserializeRequest(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        GrainType grainType = new(reader.ReadString());
        string key = reader.ReadString();
        uint methodId = reader.ReadVarUInt32();
        uint argCount = reader.ReadVarUInt32();

        object?[] arguments = new object?[argCount];
        for (int i = 0; i < argCount; i++)
            arguments[i] = ReadValue(reader);

        return new GrainInvocationRequest(new GrainId(grainType, key), methodId, arguments);
    }

    /// <summary>Serializes a grain invocation response.</summary>
    public byte[] SerializeResponse(GrainInvocationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);
        writer.WriteByte(response.Success ? (byte)1 : (byte)0);
        WriteValue(writer, response.Result);
        WriteValue(writer, response.Error);
        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Deserializes a grain invocation response.</summary>
    public GrainInvocationResponse DeserializeResponse(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        bool success = reader.ReadByte() == 1;
        object? result = ReadValue(reader);
        string? error = ReadValue(reader) as string;
        return new GrainInvocationResponse(success, result, error);
    }

    private static void WriteValue(CodecWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteByte((byte)ValueKind.Null);
                break;
            case bool b:
                writer.WriteByte((byte)ValueKind.Boolean);
                writer.WriteByte(b ? (byte)1 : (byte)0);
                break;
            case int i32:
                writer.WriteByte((byte)ValueKind.Int32);
                writer.WriteInt32(i32);
                break;
            case uint u32:
                writer.WriteByte((byte)ValueKind.UInt32);
                writer.WriteVarUInt32(u32);
                break;
            case long i64:
                writer.WriteByte((byte)ValueKind.Int64);
                writer.WriteInt64(i64);
                break;
            case ulong u64:
                writer.WriteByte((byte)ValueKind.UInt64);
                writer.WriteVarUInt64(u64);
                break;
            case string s:
                writer.WriteByte((byte)ValueKind.String);
                writer.WriteString(s);
                break;
            case Guid guid:
                writer.WriteByte((byte)ValueKind.Guid);
                writer.WriteRaw(guid.ToByteArray());
                break;
            case byte[] bytes:
                writer.WriteByte((byte)ValueKind.ByteArray);
                writer.WriteBytes(bytes);
                break;
            case double dbl:
                writer.WriteByte((byte)ValueKind.Double);
                writer.WriteFixed64((ulong)BitConverter.DoubleToInt64Bits(dbl));
                break;
            case float sgl:
                writer.WriteByte((byte)ValueKind.Single);
                writer.WriteFixed32((uint)BitConverter.SingleToInt32Bits(sgl));
                break;
            case decimal dec:
                writer.WriteByte((byte)ValueKind.Decimal);
                foreach (int part in decimal.GetBits(dec))
                    writer.WriteInt32(part);
                break;
            default:
                throw new NotSupportedException(
                    $"The transport message serializer does not support values of type '{value.GetType().FullName}'.");
        }
    }

    private static object? ReadValue(CodecReader reader)
    {
        ValueKind kind = (ValueKind)reader.ReadByte();
        return kind switch
        {
            ValueKind.Null => null,
            ValueKind.Boolean => reader.ReadByte() == 1,
            ValueKind.Int32 => reader.ReadInt32(),
            ValueKind.UInt32 => reader.ReadVarUInt32(),
            ValueKind.Int64 => reader.ReadInt64(),
            ValueKind.UInt64 => reader.ReadVarUInt64(),
            ValueKind.String => reader.ReadString(),
            ValueKind.Guid => new Guid(reader.ReadRaw(16)),
            ValueKind.ByteArray => reader.ReadBytes(),
            ValueKind.Double => BitConverter.Int64BitsToDouble((long)reader.ReadFixed64()),
            ValueKind.Single => BitConverter.Int32BitsToSingle((int)reader.ReadFixed32()),
            ValueKind.Decimal => new decimal([
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()]),
            _ => throw new NotSupportedException($"Unsupported serialized value kind '{kind}'.")
        };
    }
}

/// <summary>
/// Routes incoming message envelopes to the appropriate grain activation.
/// </summary>
public interface IMessageDispatcher
{
    /// <summary>Processes a message envelope and returns an optional response envelope.</summary>
    Task<MessageEnvelope?> DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default dispatcher for request/one-way message envelopes.
/// </summary>
public sealed class MessageDispatcher : IMessageDispatcher
{
    private readonly IGrainCallInvoker _invoker;
    private readonly GrainMessageSerializer _serializer;

    /// <summary>Initializes the dispatcher.</summary>
    public MessageDispatcher(IGrainCallInvoker invoker, GrainMessageSerializer serializer)
    {
        _invoker = invoker;
        _serializer = serializer;
    }

    /// <inheritdoc/>
    public async Task<MessageEnvelope?> DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        switch (envelope.MessageType)
        {
            case MessageType.Request:
                return await DispatchRequestAsync(envelope, expectResponse: true, cancellationToken).ConfigureAwait(false);
            case MessageType.OneWayRequest:
                _ = await DispatchRequestAsync(envelope, expectResponse: false, cancellationToken).ConfigureAwait(false);
                return null;
            default:
                return null;
        }
    }

    private async Task<MessageEnvelope?> DispatchRequestAsync(
        MessageEnvelope envelope,
        bool expectResponse,
        CancellationToken cancellationToken)
    {
        GrainInvocationRequest request = _serializer.DeserializeRequest(envelope.Payload);

        try
        {
            if (!expectResponse)
            {
                await _invoker.InvokeVoidAsync(request.GrainId, request.MethodId, request.Arguments, cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            object? result = await _invoker.InvokeAsync(request.GrainId, request.MethodId, request.Arguments, cancellationToken)
                .ConfigureAwait(false);

            GrainInvocationResponse response = new(true, result, null);
            return new MessageEnvelope
            {
                CorrelationId = envelope.CorrelationId,
                MessageType = MessageType.Response,
                Headers = envelope.Headers,
                Payload = _serializer.SerializeResponse(response)
            };
        }
        catch (Exception ex) when (expectResponse)
        {
            GrainInvocationResponse response = new(false, null, ex.ToString());
            return new MessageEnvelope
            {
                CorrelationId = envelope.CorrelationId,
                MessageType = MessageType.Response,
                Headers = envelope.Headers,
                Payload = _serializer.SerializeResponse(response)
            };
        }
    }
}

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
