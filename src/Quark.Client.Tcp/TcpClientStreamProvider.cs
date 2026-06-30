using System.Collections.Concurrent;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Streaming.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     <see cref="IStreamProvider" /> for the TCP gateway client.
///     Hands out <see cref="TcpClientStream{T}" /> instances keyed by (<see cref="StreamId" />, item type).
/// </summary>
public sealed class TcpClientStreamProvider : IStreamProvider
{
    private readonly TcpGatewayConnection _connection;
    private readonly TcpStreamPushDispatcher _dispatcher;
    private readonly ICodecProvider _codecProvider;
    private readonly ConcurrentDictionary<(StreamId, Type), object> _streams = new();

    public string Name { get; }

    public TcpClientStreamProvider(
        string name,
        TcpGatewayConnection connection,
        TcpStreamPushDispatcher dispatcher,
        ICodecProvider codecProvider)
    {
        Name = name;
        _connection = connection;
        _dispatcher = dispatcher;
        _codecProvider = codecProvider;
    }

    public IAsyncStream<T> GetStream<T>(StreamId streamId)
    {
        return (IAsyncStream<T>)_streams.GetOrAdd((streamId, typeof(T)),
            ValueFactory);

        object ValueFactory((StreamId, Type) _) => new TcpClientStream<T>(streamId, _connection, _dispatcher, _codecProvider.GetRequiredCodec<T>());
    }
}
