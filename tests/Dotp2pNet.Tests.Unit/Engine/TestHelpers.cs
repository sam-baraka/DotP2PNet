using System.Net;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Tests.Unit.Engine;

internal class TestConnectionManager : IConnectionManager
{
    public IReadOnlyCollection<IPeerConnection> ActiveConnections => new List<IPeerConnection>();
    public Task<IPeerConnection> ConnectToPeerAsync(string ipAddress, int port, CancellationToken ct) => 
        Task.FromResult<IPeerConnection>(new TestPeerConnection());
    public Task<IPeerConnection> AcceptConnectionAsync(CancellationToken ct) => 
        Task.FromResult<IPeerConnection>(new TestPeerConnection());
    public Task StartListeningAsync(int port, CancellationToken ct) => Task.CompletedTask;
    public Task StopListeningAsync() => Task.CompletedTask;
    public Task RemoveConnectionAsync(IPeerConnection connection) => Task.CompletedTask;
    public Task CloseAllConnectionsAsync() => Task.CompletedTask;
}

internal class TestPeerConnection : IPeerConnection
{
    public string PeerId => "test-peer";
    public bool IsConnected => true;
    public bool AmChoking => false;
    public bool PeerChoking => false;
    public bool AmInterested => false;
    public bool PeerInterested => false;
    public Task HandshakeAsync(byte[] infoHash, byte[] peerId, CancellationToken ct) => Task.CompletedTask;
    public Task SendMessageAsync(byte messageType, byte[] payload, CancellationToken ct) => Task.CompletedTask;
    public Task<(byte messageType, byte[] payload)> ReceiveMessageAsync(CancellationToken ct) => 
        Task.FromResult<(byte, byte[])>((0, Array.Empty<byte>()));
    public Task CloseAsync() => Task.CompletedTask;
    public void Dispose() { }
}

internal class TestTrackerClient : ITrackerClient
{
    public Task<TrackerResponse> AnnounceAsync(string trackerUrl, byte[] infoHash, byte[] peerId, int port, 
        long downloaded, long uploaded, long left, TrackerEvent eventType, CancellationToken ct = default)
    {
        return Task.FromResult(new TrackerResponse
        {
            Interval = 1800,
            Complete = 5,
            Incomplete = 10,
            Peers = new List<PeerInfo>()
        });
    }

    public Task<List<PeerInfo>> GetPeersAsync(string trackerUrl, byte[] infoHash, byte[] peerId, int port, 
        CancellationToken ct = default)
    {
        return Task.FromResult(new List<PeerInfo>());
    }
}

internal class TestDhtNode : IDhtNode
{
    public byte[] NodeId => new byte[20];
    public bool IsRunning => false;
    public int Port => 6881;
    public Task StartAsync(int port, CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task BootstrapAsync(List<PeerInfo> bootstrapNodes, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<PeerInfo>> FindPeersAsync(byte[] infoHash, CancellationToken ct = default) => 
        Task.FromResult(new List<PeerInfo>());
    public Task AnnouncePeerAsync(byte[] infoHash, int port, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> PingAsync(byte[] nodeId, IPAddress address, int port, CancellationToken ct = default) => 
        Task.FromResult(true);
    public Task<List<PeerInfo>> FindNodeAsync(byte[] targetId, CancellationToken ct = default) => 
        Task.FromResult(new List<PeerInfo>());
    public int GetRoutingTableSize() => 0;
}

internal class TestPieceManager : IPieceManager
{
    public int TotalPieces => 4;
    public int CompletedPieces => 0;
    public int PieceLength => 256 * 1024;
    public bool HasPiece(int pieceIndex) => false;
    public int SelectPiece(IBitfield peerBitfield) => -1;
    public Task<bool> StorePieceAsync(int pieceIndex, byte[] data, CancellationToken ct) => Task.FromResult(true);
    public Task<byte[]> GetPieceAsync(int pieceIndex, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
    public IBitfield GetBitfield() => new TestBitfield();
}

internal class TestBitfield : IBitfield
{
    public int Length => 4;
    public bool HasPiece(int pieceIndex) => false;
    public void SetPiece(int pieceIndex) { }
    public void ClearPiece(int pieceIndex) { }
    public byte[] ToBytes() => Array.Empty<byte>();
    public int CountSetBits() => 0;
}

internal class TestPieceSelector : IPieceSelector
{
    public int? SelectNextPiece(IBitfield localBitfield, Dictionary<Guid, IBitfield> peerBitfields) => null;
}

internal class TestPeerSelector : IPeerSelector
{
    public List<PeerInfo> SelectPeersToConnect(List<PeerInfo> availablePeers, List<PeerInfo> currentConnections, 
        int maxConnections) => new List<PeerInfo>();
    public List<byte[]> SelectPeersToUnchoke(List<PeerInfo> connectedPeers, int uploadSlots) => new List<byte[]>();
    public byte[]? SelectOptimisticUnchoke(List<PeerInfo> connectedPeers, List<byte[]> currentlyUnchoked) => null;
}

internal class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, 
        Func<TState, Exception?, string> formatter) { }
}
