using System.IO.Pipelines;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Runtime;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Resource-exhaustion coverage for the inbound connection cap (issue #55 [A2]):
///     <see cref="SiloMessagePump"/>'s accept loop must stop admitting connections once
///     <see cref="TransportOptions.MaxConnections"/> are concurrently open, closing the excess
///     immediately instead of spawning an unbounded number of per-connection processing loops.
///     Driven through a fully in-memory fake transport — no real sockets, fully deterministic.
/// </summary>
public sealed class ConnectionLimitTests
{
    [Fact]
    public async Task AcceptLoop_Closes_Connections_Beyond_MaxConnections()
    {
        const int max = 2;
        const int offered = 5;

        var connections = new List<FakeConnection>();
        var decided = new CountdownEvent(offered);
        for (int i = 0; i < offered; i++)
        {
            connections.Add(new FakeConnection(i.ToString(), decided));
        }

        var listener = new FakeListener(connections);
        var transport = new FakeTransport(listener);
        var services = new SingleServiceProvider(typeof(ITransport), transport);

        await using var pump = new SiloMessagePump(
            services,
            new MessageSerializer(),
            new NullDispatcher(),
            Options.Create(new SiloRuntimeOptions()),
            NullLogger<SiloMessagePump>.Instance,
            new TransportOptions { MaxConnections = max });

        await pump.StartAsync();

        // Wait until every offered connection has been admitted or rejected.
        Assert.True(decided.Wait(TimeSpan.FromSeconds(10)), "accept loop did not decide all connections");

        int admitted = connections.Count(c => c.Executed);
        int rejected = connections.Count(c => c is { Executed: false, Closed: true });

        Assert.Equal(max, admitted);
        Assert.Equal(offered - max, rejected);
        Assert.All(connections.Where(c => !c.Executed), c =>
        {
            Assert.True(c.Closed, "rejected connection was not closed");
            Assert.True(c.Disposed, "rejected connection was not disposed");
        });

        // Admitted fake connections block on ReadAsync; shutdown cancels them, which the in-memory
        // pipe surfaces as OperationCanceledException (a real socket would observe EOF). Expected.
        try
        {
            await pump.StopAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---- fakes ----------------------------------------------------------

    private sealed class NullDispatcher : IMessageDispatcher
    {
        public Task<MessageEnvelope?> DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
            => Task.FromResult<MessageEnvelope?>(null);
    }

    private sealed class SingleServiceProvider(Type serviceType, object instance) : IServiceProvider
    {
        public object? GetService(Type serviceType_) => serviceType_ == serviceType ? instance : null;
    }

    private sealed class FakeTransport(FakeListener listener) : ITransport
    {
        public string Name => "fake";
        public ITransportListener CreateListener(EndPoint endPoint) => listener;
        public Task<ITransportConnection> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeListener(List<FakeConnection> connections) : ITransportListener
    {
        private int _index;

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 11111);
        public Task BindAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask<ITransportConnection?> AcceptAsync(CancellationToken cancellationToken = default)
        {
            int i = _index++;
            // Hand out every queued connection, then signal end-of-stream so the loop exits cleanly.
            return ValueTask.FromResult<ITransportConnection?>(i < connections.Count ? connections[i] : null);
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeConnection : ITransportConnection
    {
        private readonly CountdownEvent _decided;
        private int _signalled;
        private readonly Pipe _inbound = new();
        private readonly Pipe _outbound = new();
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeConnection(string id, CountdownEvent decided)
        {
            ConnectionId = id;
            _decided = decided;
            Transport = new DuplexPipe(_inbound.Reader, _outbound.Writer);
        }

        public string ConnectionId { get; }
        public EndPoint? LocalEndPoint => null;
        public EndPoint? RemoteEndPoint => null;
        public IDuplexPipe Transport { get; }
        public Task Completion => _completion.Task;
        public bool Executed { get; private set; }
        public bool Closed { get; private set; }
        public bool Disposed { get; private set; }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            Executed = true;
            Decide();
            // Admitted: stay alive until the pump cancels at shutdown.
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { }
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Closed = true;
            Decide();
            _completion.TrySetResult();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private void Decide()
        {
            if (Interlocked.Exchange(ref _signalled, 1) == 0)
            {
                _decided.Signal();
            }
        }

        private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
        {
            public PipeReader Input { get; } = input;
            public PipeWriter Output { get; } = output;
        }
    }
}
