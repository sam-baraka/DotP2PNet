using System.Numerics;

namespace Dotp2pNet.Discovery.Dht;

/// <summary>
/// Provides utilities for calculating XOR distance in the Kademlia DHT.
/// </summary>
/// <remarks>
/// In Kademlia, distance between two node IDs is calculated using XOR.
/// This creates a metric space where distance is symmetric and follows
/// the triangle inequality, making it ideal for distributed routing.
/// </remarks>
public static class XorDistance
{
    /// <summary>
    /// Calculates the XOR distance between two 160-bit node IDs.
    /// </summary>
    /// <param name="id1">The first node ID (20 bytes).</param>
    /// <param name="id2">The second node ID (20 bytes).</param>
    /// <returns>The XOR distance as a BigInteger.</returns>
    public static BigInteger Calculate(byte[] id1, byte[] id2)
    {
        if (id1.Length != 20 || id2.Length != 20)
            throw new ArgumentException("Node IDs must be exactly 20 bytes (160 bits)");

        byte[] xor = new byte[20];
        for (int i = 0; i < 20; i++)
        {
            xor[i] = (byte)(id1[i] ^ id2[i]);
        }

        // Convert to BigInteger (add 0 byte to ensure positive value)
        return new BigInteger(xor.Concat(new byte[] { 0 }).ToArray());
    }

    /// <summary>
    /// Calculates the bucket index for a given distance.
    /// </summary>
    /// <param name="distance">The XOR distance.</param>
    /// <returns>The bucket index (0-159).</returns>
    /// <remarks>
    /// The bucket index is determined by the position of the most significant bit.
    /// Bucket 0 contains nodes with distance 2^0 to 2^1 - 1,
    /// Bucket 1 contains nodes with distance 2^1 to 2^2 - 1, etc.
    /// </remarks>
    public static int GetBucketIndex(BigInteger distance)
    {
        if (distance.IsZero)
            return 0;

        // Find the position of the most significant bit
        int bitLength = (int)Math.Floor(BigInteger.Log(distance, 2));
        return Math.Min(bitLength, 159);
    }

    /// <summary>
    /// Calculates the bucket index for a node ID relative to our node ID.
    /// </summary>
    ///  /// <param name="ou">Our node ID (20 bytes).</param>
    /// <param name="theirNodeId">Their node ID (20 bytes).</param>
    /// <returns>The bucket index (0-159).</returns>
    public static int GetBucketIndex(byte[] ourNodeId, byte[] theirNodeId)
    {
        var distance = Calculate(ourNodeId, theirNodeId);
        return GetBucketIndex(distance);
    }

    /// <summary>
    /// Compares two node IDs by their distance to a target ID.
    /// </summary>
    /// <param name="targetId">The target ID to compare distances to.</param>
    /// <param name="id1">The first node ID.</param>
    /// <param name="id2">The second node ID.</param>
    /// <returns>
    /// -1 if id1 is closer to target than id2,
    /// 0 if they are equidistant,
    /// 1 if id2 is closer to target than id1.
    /// </returns>
    public static int CompareDistance(byte[] targetId, byte[] id1, byte[] id2)
    {
        var distance1 = Calculate(targetId, id1);
        var distance2 = Calculate(targetId, id2);
        return distance1.CompareTo(distance2);
    }

    /// <summary>
    /// Sorts a list of node IDs by their distance to a target ID (closest first).
    /// </summary>
    /// <param name="targetId">The target ID to sort by distance to.</param>
    /// <param name="nodeIds">The list of node IDs to sort.</param>
    /// <returns>The sorted list of node IDs.</returns>
    public static List<byte[]> SortByDistance(byte[] targetId, IEnumerable<byte[]> nodeIds)
    {
        return nodeIds
            .OrderBy(id => Calculate(targetId, id))
            .ToList();
    }
}
