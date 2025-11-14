using System.Net;

namespace Dotp2pNet.Discovery.Dht;

/// <summary>
/// Represents information about a node in the DHT network.
/// </summary>
public class DhtNodeInfo
{
    /// <summary>
    /// Gets or sets the 160-bit node ID.
    /// </summary>
    public byte[] NodeId { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the IP address of the node.
    /// </summary>
    public IPAddress IpAddress { get; set; } = IPAddress.None;

    /// <summary>
    /// Gets or sets the UDP port the node is listening on.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the last time this node was seen or responded.
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the number of consecutive failures when contacting this node.
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Gets a value indicating whether this node is considered good.
    /// A node is good if it has responded recently and has few failures.
    /// </summary>
    public bool IsGood => FailureCount < 3 && (DateTime.UtcNow - LastSeen) < TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets a value indicating whether this node is questionable.
    /// A node is questionable if it hasn't responded recently.
    /// </summary>
    public bool IsQuestionable => !IsGood && FailureCount < 3;

    /// <summary>
    /// Gets a value indicating whether this node is bad.
    /// A node is bad if it has failed multiple times.
    /// </summary>
    public bool IsBad => FailureCount >= 3;

    /// <summary>
    /// Updates the last seen timestamp and resets failure count.
    /// </summary>
    public void MarkSeen()
    {
        LastSeen = DateTime.UtcNow;
        FailureCount = 0;
    }

    /// <summary>
    /// Increments the failure count for this node.
    /// </summary>
    public void MarkFailed()
    {
        FailureCount++;
    }

    /// <summary>
    /// Returns a string representation of this DHT node.
    /// </summary>
    public override string ToString()
    {
        var nodeIdHex = NodeId.Length >= 4
            ? BitConverter.ToString(NodeId[..4]).Replace("-", "").ToLower()
            : "unknown";
        return $"{nodeIdHex}...@{IpAddress}:{Port}";
    }
}