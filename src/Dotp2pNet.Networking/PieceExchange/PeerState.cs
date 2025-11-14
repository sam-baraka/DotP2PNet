using Dotp2pNet.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Networking.PieceExchange;

/// <summary>
/// Tracks the state of a peer connection in the piece exchange protocol.
/// </summary>
/// <remarks>
/// Peer state includes:
/// - Choking state: Whether we/they are allowing uploads
/// - Interest state: Whether we/they want to download
/// - Bitfield: Which pieces the peer has available
/// 
/// State transitions follow the BitTorrent protocol:
/// - Peers start choked and not interested
/// - Interest is expressed when peer has pieces we need
/// - Unchoking allows piece requests to be fulfilled
/// </remarks>
public class PeerState
{
    private readonly ILogger<PeerState> _logger;
    private readonly string _peerId;
    private readonly object _stateLock = new();

    private bool _amChoking;
    private bool _amInterested;
    private bool _peerChoking;
    private bool _peerInterested;
    private IBitfield? _peerBitfield;

    /// <summary>
    /// Gets a value indicating whether we are choking this peer.
    /// </summary>
    public bool AmChoking
    {
        get
        {
            lock (_stateLock)
            {
                return _amChoking;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether we are interested in this peer's pieces.
    /// </summary>
    public bool AmInterested
    {
        get
        {
            lock (_stateLock)
            {
                return _amInterested;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this peer is choking us.
    /// </summary>
    public bool PeerChoking
    {
        get
        {
            lock (_stateLock)
            {
                return _peerChoking;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this peer is interested in our pieces.
    /// </summary>
    public bool PeerInterested
    {
        get
        {
            lock (_stateLock)
            {
                return _peerInterested;
            }
        }
    }

    /// <summary>
    /// Gets the peer's bitfield indicating which pieces they have.
    /// </summary>
    public IBitfield? PeerBitfield
    {
        get
        {
            lock (_stateLock)
            {
                return _peerBitfield;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the PeerState class.
    /// </summary>
    /// <param name="peerId">The peer identifier.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public PeerState(string peerId, ILogger<PeerState> logger)
    {
        _peerId = peerId ?? throw new ArgumentNullException(nameof(peerId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initial state: both peers are choked and not interested
        _amChoking = true;
        _amInterested = false;
        _peerChoking = true;
        _peerInterested = false;
    }

    /// <summary>
    /// Sets whether we are choking this peer.
    /// </summary>
    /// <param name="choking">True to choke, false to unchoke.</param>
    public void SetAmChoking(bool choking)
    {
        lock (_stateLock)
        {
            if (_amChoking != choking)
            {
                _amChoking = choking;
                _logger.LogInformation(
                    "State change for peer {PeerId}: we are now {State} them",
                    _peerId,
                    choking ? "choking" : "unchoking");
            }
        }
    }

    /// <summary>
    /// Sets whether we are interested in this peer's pieces.
    /// </summary>
    /// <param name="interested">True if interested, false otherwise.</param>
    public void SetAmInterested(bool interested)
    {
        lock (_stateLock)
        {
            if (_amInterested != interested)
            {
                _amInterested = interested;
                _logger.LogInformation(
                    "State change for peer {PeerId}: we are now {State} in their pieces",
                    _peerId,
                    interested ? "interested" : "not interested");
            }
        }
    }

    /// <summary>
    /// Sets whether this peer is choking us.
    /// </summary>
    /// <param name="choking">True if peer is choking us, false otherwise.</param>
    public void SetPeerChoking(bool choking)
    {
        lock (_stateLock)
        {
            if (_peerChoking != choking)
            {
                _peerChoking = choking;
                _logger.LogInformation(
                    "State change for peer {PeerId}: they are now {State} us",
                    _peerId,
                    choking ? "choking" : "unchoking");
            }
        }
    }

    /// <summary>
    /// Sets whether this peer is interested in our pieces.
    /// </summary>
    /// <param name="interested">True if peer is interested, false otherwise.</param>
    public void SetPeerInterested(bool interested)
    {
        lock (_stateLock)
        {
            if (_peerInterested != interested)
            {
                _peerInterested = interested;
                _logger.LogInformation(
                    "State change for peer {PeerId}: they are now {State} in our pieces",
                    _peerId,
                    interested ? "interested" : "not interested");
            }
        }
    }

    /// <summary>
    /// Sets the peer's bitfield.
    /// </summary>
    /// <param name="bitfield">The peer's bitfield.</param>
    public void SetPeerBitfield(IBitfield bitfield)
    {
        lock (_stateLock)
        {
            _peerBitfield = bitfield;
            _logger.LogInformation(
                "Received bitfield from peer {PeerId}: {PieceCount} pieces, {Available} available",
                _peerId,
                bitfield.Length,
                bitfield.CountSetBits());
        }
    }

    /// <summary>
    /// Updates the peer's bitfield to indicate they have a new piece.
    /// </summary>
    /// <param name="pieceIndex">The index of the piece the peer now has.</param>
    public void SetPeerHasPiece(int pieceIndex)
    {
        lock (_stateLock)
        {
            if (_peerBitfield != null)
            {
                _peerBitfield.SetPiece(pieceIndex);
                _logger.LogDebug(
                    "Peer {PeerId} now has piece {PieceIndex} ({Available}/{Total} pieces)",
                    _peerId,
                    pieceIndex,
                    _peerBitfield.CountSetBits(),
                    _peerBitfield.Length);
            }
        }
    }

    /// <summary>
    /// Gets a summary of the current peer state.
    /// </summary>
    /// <returns>String representation of the peer state.</returns>
    public string GetStateSummary()
    {
        lock (_stateLock)
        {
            var piecesAvailable = _peerBitfield?.CountSetBits() ?? 0;
            var totalPieces = _peerBitfield?.Length ?? 0;

            return $"Peer {_peerId}: " +
                   $"AmChoking={_amChoking}, AmInterested={_amInterested}, " +
                   $"PeerChoking={_peerChoking}, PeerInterested={_peerInterested}, " +
                   $"Pieces={piecesAvailable}/{totalPieces}";
        }
    }
}
