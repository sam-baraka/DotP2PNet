using System.Net;
using System.Net.Sockets;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Discovery.NatTraversal;

public class NatTraversal : INatTraversal
{
    private readonly ILogger<NatTraversal> _logger;
    
    private static readonly string[] StunServers = new[]
    {
        "stun.l.google.com:19302",
        "stun1.l.google.com:19302",
        "stun2.l.google.com:19302"
    };

    private const ushort StunBindingRequest = 0x0001;
    private const uint StunMagicCookie = 0x2112A442;
    private const int StunHeaderSize = 20;
    private const int StunTransactionIdSize = 12;
    private const ushort StunAttrMappedAddress = 0x0001;
    private const ushort StunAttrXorMappedAddress = 0x0020;

    public NatTraversal(ILogger<NatTraversal> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IPAddress?> GetExternalIpAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting STUN external IP discovery");

        foreach (var serverAddress in StunServers)
        {
            try
            {
                var parts = serverAddress.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                {
                    _logger.LogWarning("Invalid STUN server address format: {Server}", serverAddress);
                    continue;
                }

                var host = parts[0];
                _logger.LogDebug("Attempting STUN query to {Server}", serverAddress);

                var externalIp = await QueryStunServerAsync(host, port, ct).ConfigureAwait(false);
                
                if (externalIp != null)
                {
                    _logger.LogInformation("Successfully discovered external IP: {ExternalIp} via {Server}", 
                        externalIp, serverAddress);
                    return externalIp;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to query STUN server {Server}", serverAddress);
            }
        }

        _logger.LogError("Failed to discover external IP from any STUN server");
        return null;
    }

    public Task<bool> TryUpnpPortMappingAsync(int internalPort, int externalPort, CancellationToken ct = default)
    {
        _logger.LogInformation("UPnP port mapping not yet implemented (Task 19)");
        return Task.FromResult(false);
    }

    public Task<bool> TryUdpHolePunchingAsync(PeerInfo remotePeer, CancellationToken ct = default)
    {
        _logger.LogInformation("UDP hole punching not yet implemented (Task 20)");
        return Task.FromResult(false);
    }

    private async Task<IPAddress?> QueryStunServerAsync(string host, int port, CancellationToken ct)
    {
        using var udpClient = new UdpClient();
        
        try
        {
            udpClient.Client.ReceiveTimeout = 3000;
            udpClient.Client.SendTimeout = 3000;

            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            var serverAddress = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            
            if (serverAddress == null)
            {
                _logger.LogWarning("No IPv4 address found for STUN server {Host}", host);
                return null;
            }

            var serverEndpoint = new IPEndPoint(serverAddress, port);
            var request = CreateStunBindingRequest();
            var transactionId = ExtractTransactionId(request);

            _logger.LogDebug("Sending STUN binding request to {Endpoint}", serverEndpoint);
            
            await udpClient.SendAsync(request, request.Length, serverEndpoint).ConfigureAwait(false);

            var receiveTask = udpClient.ReceiveAsync();
            var timeoutTask = Task.Delay(3000, ct);
            var completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("STUN query to {Endpoint} timed out", serverEndpoint);
                return null;
            }

            var result = await receiveTask.ConfigureAwait(false);
            var response = result.Buffer;

            _logger.LogDebug("Received STUN response ({Bytes} bytes) from {Endpoint}", 
                response.Length, serverEndpoint);

            return ParseStunResponse(response, transactionId);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "Socket error during STUN query to {Host}:{Port}", host, port);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error during STUN query to {Host}:{Port}", host, port);
            return null;
        }
    }

    private byte[] CreateStunBindingRequest()
    {
        var request = new byte[StunHeaderSize];
        
        request[0] = (byte)(StunBindingRequest >> 8);
        request[1] = (byte)(StunBindingRequest & 0xFF);

        request[2] = 0;
        request[3] = 0;

        var magicCookie = BitConverter.GetBytes(StunMagicCookie);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(magicCookie);
        Buffer.BlockCopy(magicCookie, 0, request, 4, 4);

        var transactionId = new byte[StunTransactionIdSize];
        Random.Shared.NextBytes(transactionId);
        Buffer.BlockCopy(transactionId, 0, request, 8, StunTransactionIdSize);

        return request;
    }

    private byte[] ExtractTransactionId(byte[] message)
    {
        var transactionId = new byte[StunTransactionIdSize];
        Buffer.BlockCopy(message, 8, transactionId, 0, StunTransactionIdSize);
        return transactionId;
    }

    private IPAddress? ParseStunResponse(byte[] response, byte[] expectedTransactionId)
    {
        try
        {
            if (response.Length < StunHeaderSize)
            {
                _logger.LogWarning("STUN response too short: {Length} bytes", response.Length);
                return null;
            }

            var messageType = (ushort)((response[0] << 8) | response[1]);
            if (messageType != 0x0101)
            {
                _logger.LogWarning("Unexpected STUN message type: 0x{Type:X4}", messageType);
                return null;
            }

            var magicCookie = (uint)((response[4] << 24) | (response[5] << 16) | 
                                    (response[6] << 8) | response[7]);
            if (magicCookie != StunMagicCookie)
            {
                _logger.LogWarning("Invalid STUN magic cookie: 0x{Cookie:X8}", magicCookie);
                return null;
            }

            var transactionId = new byte[StunTransactionIdSize];
            Buffer.BlockCopy(response, 8, transactionId, 0, StunTransactionIdSize);
            if (!transactionId.SequenceEqual(expectedTransactionId))
            {
                _logger.LogWarning("STUN transaction ID mismatch");
                return null;
            }

            var messageLength = (ushort)((response[2] << 8) | response[3]);
            
            var offset = StunHeaderSize;
            while (offset + 4 <= response.Length && offset < StunHeaderSize + messageLength)
            {
                var attrType = (ushort)((response[offset] << 8) | response[offset + 1]);
                var attrLength = (ushort)((response[offset + 2] << 8) | response[offset + 3]);
                offset += 4;

                if (offset + attrLength > response.Length)
                {
                    _logger.LogWarning("Invalid STUN attribute length");
                    break;
                }

                if (attrType == StunAttrXorMappedAddress)
                {
                    var ip = ParseXorMappedAddress(response, offset, attrLength, transactionId);
                    if (ip != null)
                        return ip;
                }
                else if (attrType == StunAttrMappedAddress)
                {
                    var ip = ParseMappedAddress(response, offset, attrLength);
                    if (ip != null)
                        return ip;
                }

                offset += attrLength;
                var padding = (4 - (attrLength % 4)) % 4;
                offset += padding;
            }

            _logger.LogWarning("No mapped address found in STUN response");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing STUN response");
            return null;
        }
    }

    private IPAddress? ParseMappedAddress(byte[] response, int offset, int length)
    {
        try
        {
            if (length < 8)
                return null;

            var family = response[offset + 1];
            
            if (family != 0x01)
            {
                _logger.LogDebug("Unsupported address family in MAPPED-ADDRESS: {Family}", family);
                return null;
            }

            var ipBytes = new byte[4];
            Buffer.BlockCopy(response, offset + 4, ipBytes, 0, 4);
            
            return new IPAddress(ipBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing MAPPED-ADDRESS attribute");
            return null;
        }
    }

    private IPAddress? ParseXorMappedAddress(byte[] response, int offset, int length, byte[] transactionId)
    {
        try
        {
            if (length < 8)
                return null;

            var family = response[offset + 1];
            
            if (family != 0x01)
            {
                _logger.LogDebug("Unsupported address family in XOR-MAPPED-ADDRESS: {Family}", family);
                return null;
            }

            var xorIpBytes = new byte[4];
            Buffer.BlockCopy(response, offset + 4, xorIpBytes, 0, 4);

            var magicCookieBytes = BitConverter.GetBytes(StunMagicCookie);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(magicCookieBytes);

            var ipBytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                ipBytes[i] = (byte)(xorIpBytes[i] ^ magicCookieBytes[i]);
            }

            return new IPAddress(ipBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing XOR-MAPPED-ADDRESS attribute");
            return null;
        }
    }
}
