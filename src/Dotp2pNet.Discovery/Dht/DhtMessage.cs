using System.Net;

namespace Dotp2pNet.Discovery.Dht;

/// <summary>
/// Represents the type of DHT message.
/// </summary>
public enum DhtMessageType
{
    /// <summary>
    /// Query message (request).
    /// </summary>
    Query,

    /// <summary>
    /// Response message (reply to query).
    /// </summary>
    Response,

    /// <summary>
    /// Error message.
    /// </summary>
    Error
}

/// <summary>
/// Represents the method being called in a DHT RPC.
/// </summary>
public enum DhtMethod
{
    /// <summary>
    /// Ping - check if a node is alive.
    /// </summary>
    Ping,

    /// <summary>
    /// Find node - find closest nodes to a target ID.
    /// </summary>
    FindNode,

    /// <summary>
    /// Get peers - find peers sharing a file.
    /// </summary>
    GetPeers,

    /// <summary>
    /// Announce peer - announce that we're sharing a file.
    /// </summary>
    AnnouncePeer
}

/// <summary>
/// Base class for all DHT messages.
/// </summary>
public abstract class DhtMessage
{
    /// <summary>
    /// Gets or sets the transaction ID for matching requests and responses.
    /// </summary>
    public byte[] TransactionId { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets the message type.
    /// </summary>
    public abstract DhtMessageType MessageType { get; }
}

/// <summary>
/// Represents a DHT query (request) message.
/// </summary>
public class DhtQuery : DhtMessage
{
    /// <summary>
    /// Gets the message type (Query).
    /// </summary>
    public override DhtMessageType MessageType => DhtMessageType.Query;

    /// <summary>
    /// Gets or sets the querying node's ID.
    /// </summary>
    public byte[] QueryingNodeId { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the RPC method being called.
    /// </summary>
    public DhtMethod Method { get; set; }

    /// <summary>
    /// Gets or sets the target ID (for find_node and get_peers).
    /// </summary>
    public byte[]? TargetId { get; set; }

    /// <summary>
    /// Gets or sets the info hash (for get_peers and announce_peer).
    /// </summary>
    public byte[]? InfoHash { get; set; }

    /// <summary>
    /// Gets or sets the port (for announce_peer).
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Gets or sets the token (for announce_peer).
    /// </summary>
    public byte[]? Token { get; set; }
}

/// <summary>
/// Represents a DHT response message.
/// </summary>
public class DhtResponse : DhtMessage
{
    /// <summary>
    /// Gets the message type (Response).
    /// </summary>
    public override DhtMessageType MessageType => DhtMessageType.Response;

    /// <summary>
    /// Gets or sets the responding node's ID.
    /// </summary>
    public byte[] RespondingNodeId { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the list of nodes (for find_node and get_peers).
    /// </summary>
    public List<DhtNodeInfo>? Nodes { get; set; }

    /// <summary>
    /// Gets or sets the list of peer addresses (for get_peers).
    /// </summary>
    public List<(IPAddress Address, int Port)>? Peers { get; set; }

    /// <summary>
    /// Gets or sets the token (for get_peers, used in subsequent announce_peer).
    /// </summary>
    public byte[]? Token { get; set; }
}

/// <summary>
/// Represents a DHT error message.
/// </summary>
public class DhtError : DhtMessage
{
    /// <summary>
    /// Gets the message type (Error).
    /// </summary>
    public override DhtMessageType MessageType => DhtMessageType.Error;

    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    public int ErrorCode { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Standard DHT error codes.
/// </summary>
public static class DhtErrorCode
{
    /// <summary>
    /// Generic error.
    /// </summary>
    public const int GenericError = 201;

    /// <summary>
    /// Server error.
    /// </summary>
    public const int ServerError = 202;

    /// <summary>
    /// Protocol error (malformed packet, invalid arguments).
    /// </summary>
    public const int ProtocolError = 203;

    /// <summary>
    /// Method unknown.
    /// </summary>
    public const int MethodUnknown = 204;
}
