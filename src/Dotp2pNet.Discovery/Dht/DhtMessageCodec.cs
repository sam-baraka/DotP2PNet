using System.Net;
using System.Text;
using Dotp2pNet.Discovery.Tracker;

namespace Dotp2pNet.Discovery.Dht;

/// <summary>
/// Encodes and decodes DHT messages using bencode format.
/// </summary>
/// <remarks>
/// DHT messages use the same bencode format as BitTorrent tracker responses.
/// Messages are dictionaries with specific keys for different message types.
/// </remarks>
public class DhtMessageCodec
{

    /// <summary>
    /// Encodes a DHT message to bytes.
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded message bytes.</returns>
    public byte[] Encode(DhtMessage message)
    {
        var dict = new Dictionary<string, object>();

        // Add transaction ID
        dict["t"] = message.TransactionId;

        if (message is DhtQuery query)
        {
            dict["y"] = Encoding.UTF8.GetBytes("q");
            dict["q"] = Encoding.UTF8.GetBytes(GetMethodName(query.Method));
            
            var args = new Dictionary<string, object>
            {
                ["id"] = query.QueryingNodeId
            };

            if (query.TargetId != null)
                args["target"] = query.TargetId;

            if (query.InfoHash != null)
                args["info_hash"] = query.InfoHash;

            if (query.Port.HasValue)
                args["port"] = query.Port.Value;

            if (query.Token != null)
                args["token"] = query.Token;

            dict["a"] = args;
        }
        else if (message is DhtResponse response)
        {
            dict["y"] = Encoding.UTF8.GetBytes("r");
            
            var responseDict = new Dictionary<string, object>
            {
                ["id"] = response.RespondingNodeId
            };

            if (response.Nodes != null && response.Nodes.Count > 0)
            {
                responseDict["nodes"] = EncodeNodes(response.Nodes);
            }

            if (response.Peers != null && response.Peers.Count > 0)
            {
                responseDict["values"] = EncodePeers(response.Peers);
            }

            if (response.Token != null)
            {
                responseDict["token"] = response.Token;
            }

            dict["r"] = responseDict;
        }
        else if (message is DhtError error)
        {
            dict["y"] = Encoding.UTF8.GetBytes("e");
            dict["e"] = new List<object> { error.ErrorCode, Encoding.UTF8.GetBytes(error.ErrorMessage) };
        }

        return EncodeDictionary(dict);
    }

    /// <summary>
    /// Decodes a DHT message from bytes.
    /// </summary>
    /// <param name="data">The encoded message bytes.</param>
    /// <returns>The decoded message.</returns>
    public DhtMessage? Decode(byte[] data)
    {
        try
        {
            var dict = BencodeParser.ParseDictionary(data);
            if (dict == null)
                return null;

            var transactionId = dict.ContainsKey("t") ? (byte[])dict["t"] : Array.Empty<byte>();
            var messageType = dict.ContainsKey("y") ? Encoding.UTF8.GetString((byte[])dict["y"]) : "";

            return messageType switch
            {
                "q" => DecodeQuery(dict, transactionId),
                "r" => DecodeResponse(dict, transactionId),
                "e" => DecodeError(dict, transactionId),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private DhtQuery? DecodeQuery(Dictionary<string, object> dict, byte[] transactionId)
    {
        if (!dict.ContainsKey("q") || !dict.ContainsKey("a"))
            return null;

        var methodName = Encoding.UTF8.GetString((byte[])dict["q"]);
        var args = dict["a"] as Dictionary<string, object>;
        if (args == null)
            return null;

        var query = new DhtQuery
        {
            TransactionId = transactionId,
            Method = GetMethod(methodName),
            QueryingNodeId = args.ContainsKey("id") ? (byte[])args["id"] : Array.Empty<byte>()
        };

        if (args.ContainsKey("target"))
            query.TargetId = (byte[])args["target"];

        if (args.ContainsKey("info_hash"))
            query.InfoHash = (byte[])args["info_hash"];

        if (args.ContainsKey("port"))
            query.Port = Convert.ToInt32(args["port"]);

        if (args.ContainsKey("token"))
            query.Token = (byte[])args["token"];

        return query;
    }

    private DhtResponse? DecodeResponse(Dictionary<string, object> dict, byte[] transactionId)
    {
        if (!dict.ContainsKey("r"))
            return null;

        var responseDict = dict["r"] as Dictionary<string, object>;
        if (responseDict == null)
            return null;

        var response = new DhtResponse
        {
            TransactionId = transactionId,
            RespondingNodeId = responseDict.ContainsKey("id") ? (byte[])responseDict["id"] : Array.Empty<byte>()
        };

        if (responseDict.ContainsKey("nodes"))
        {
            response.Nodes = DecodeNodes((byte[])responseDict["nodes"]);
        }

        if (responseDict.ContainsKey("values"))
        {
            response.Peers = DecodePeers(responseDict["values"] as List<object>);
        }

        if (responseDict.ContainsKey("token"))
        {
            response.Token = (byte[])responseDict["token"];
        }

        return response;
    }

    private DhtError? DecodeError(Dictionary<string, object> dict, byte[] transactionId)
    {
        if (!dict.ContainsKey("e"))
            return null;

        var errorList = dict["e"] as List<object>;
        if (errorList == null || errorList.Count < 2)
            return null;

        return new DhtError
        {
            TransactionId = transactionId,
            ErrorCode = Convert.ToInt32(errorList[0]),
            ErrorMessage = Encoding.UTF8.GetString((byte[])errorList[1])
        };
    }

    private string GetMethodName(DhtMethod method)
    {
        return method switch
        {
            DhtMethod.Ping => "ping",
            DhtMethod.FindNode => "find_node",
            DhtMethod.GetPeers => "get_peers",
            DhtMethod.AnnouncePeer => "announce_peer",
            _ => "ping"
        };
    }

    private DhtMethod GetMethod(string methodName)
    {
        return methodName switch
        {
            "ping" => DhtMethod.Ping,
            "find_node" => DhtMethod.FindNode,
            "get_peers" => DhtMethod.GetPeers,
            "announce_peer" => DhtMethod.AnnouncePeer,
            _ => DhtMethod.Ping
        };
    }

    private byte[] EncodeNodes(List<DhtNodeInfo> nodes)
    {
        // Compact node format: 20 bytes node ID + 4 bytes IP + 2 bytes port
        var result = new List<byte>();
        foreach (var node in nodes)
        {
            result.AddRange(node.NodeId);
            result.AddRange(node.IpAddress.GetAddressBytes());
            result.Add((byte)(node.Port >> 8));
            result.Add((byte)(node.Port & 0xFF));
        }
        return result.ToArray();
    }

    private List<DhtNodeInfo> DecodeNodes(byte[] data)
    {
        var nodes = new List<DhtNodeInfo>();
        int offset = 0;

        while (offset + 26 <= data.Length)
        {
            var nodeId = data[offset..(offset + 20)];
            var ipBytes = data[(offset + 20)..(offset + 24)];
            var port = (data[offset + 24] << 8) | data[offset + 25];

            nodes.Add(new DhtNodeInfo
            {
                NodeId = nodeId,
                IpAddress = new IPAddress(ipBytes),
                Port = port
            });

            offset += 26;
        }

        return nodes;
    }

    private List<byte[]> EncodePeers(List<(IPAddress Address, int Port)> peers)
    {
        // Each peer is encoded as 6 bytes: 4 bytes IP + 2 bytes port
        var result = new List<byte[]>();
        foreach (var (address, port) in peers)
        {
            var peerBytes = new List<byte>();
            peerBytes.AddRange(address.GetAddressBytes());
            peerBytes.Add((byte)(port >> 8));
            peerBytes.Add((byte)(port & 0xFF));
            result.Add(peerBytes.ToArray());
        }
        return result;
    }

    private List<(IPAddress Address, int Port)>? DecodePeers(List<object>? values)
    {
        if (values == null)
            return null;

        var peers = new List<(IPAddress, int)>();
        foreach (var value in values)
        {
            if (value is byte[] peerBytes && peerBytes.Length == 6)
            {
                var ipBytes = peerBytes[0..4];
                var port = (peerBytes[4] << 8) | peerBytes[5];
                peers.Add((new IPAddress(ipBytes), port));
            }
        }
        return peers;
    }

    private byte[] EncodeDictionary(Dictionary<string, object> dict)
    {
        var result = new List<byte>();
        result.Add((byte)'d');

        foreach (var kvp in dict.OrderBy(k => k.Key))
        {
            result.AddRange(EncodeString(kvp.Key));
            result.AddRange(EncodeValue(kvp.Value));
        }

        result.Add((byte)'e');
        return result.ToArray();
    }

    private byte[] EncodeString(string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        return Encoding.UTF8.GetBytes($"{bytes.Length}:").Concat(bytes).ToArray();
    }

    private byte[] EncodeValue(object value)
    {
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetBytes($"{bytes.Length}:").Concat(bytes).ToArray(),
            int i => Encoding.UTF8.GetBytes($"i{i}e"),
            long l => Encoding.UTF8.GetBytes($"i{l}e"),
            string s => EncodeString(s),
            Dictionary<string, object> d => EncodeDictionary(d),
            List<object> list => EncodeList(list),
            List<byte[]> byteList => EncodeList(byteList.Cast<object>().ToList()),
            _ => Array.Empty<byte>()
        };
    }

    private byte[] EncodeList(List<object> list)
    {
        var result = new List<byte>();
        result.Add((byte)'l');

        foreach (var item in list)
        {
            result.AddRange(EncodeValue(item));
        }

        result.Add((byte)'e');
        return result.ToArray();
    }
}
