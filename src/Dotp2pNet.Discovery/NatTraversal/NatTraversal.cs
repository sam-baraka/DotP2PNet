using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
            catch (OperationCanceledException)
            {
                _logger.LogDebug("STUN query cancelled");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query STUN server {Server}", serverAddress);
            }
        }

        _logger.LogError("Failed to discover external IP from any STUN server");
        return null;
    }

    public async Task<bool> TryUpnpPortMappingAsync(int internalPort, int externalPort, CancellationToken ct = default)
    {
        _logger.LogInformation("Attempting UPnP port mapping: {InternalPort} -> {ExternalPort}", 
            internalPort, externalPort);

        try
        {
            // Step 1: Discover UPnP-enabled router using SSDP
            var gatewayUrl = await DiscoverUpnpGatewayAsync(ct).ConfigureAwait(false);
            
            if (gatewayUrl == null)
            {
                _logger.LogWarning("No UPnP-enabled router discovered");
                return false;
            }

            _logger.LogInformation("Discovered UPnP gateway at {Url}", gatewayUrl);

            // Step 2: Get the control URL from device description
            var controlUrl = await GetControlUrlAsync(gatewayUrl, ct).ConfigureAwait(false);
            
            if (controlUrl == null)
            {
                _logger.LogWarning("Failed to get UPnP control URL from gateway");
                return false;
            }

            _logger.LogDebug("UPnP control URL: {ControlUrl}", controlUrl);

            // Step 3: Get local IP address
            var localIp = GetLocalIpAddress();
            
            if (localIp == null)
            {
                _logger.LogWarning("Failed to determine local IP address");
                return false;
            }

            _logger.LogDebug("Local IP address: {LocalIp}", localIp);

            // Step 4: Add port mapping
            var success = await AddPortMappingAsync(
                controlUrl, 
                externalPort, 
                internalPort, 
                localIp, 
                ct).ConfigureAwait(false);

            if (success)
            {
                _logger.LogInformation(
                    "Successfully created UPnP port mapping: {ExternalPort} -> {LocalIp}:{InternalPort}",
                    externalPort, localIp, internalPort);
            }
            else
            {
                _logger.LogWarning("Failed to create UPnP port mapping");
            }

            return success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during UPnP port mapping");
            return false;
        }
    }

    public async Task<bool> TryUdpHolePunchingAsync(PeerInfo remotePeer, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Attempting UDP hole punching to peer {Peer}",
            remotePeer.Endpoint);

        try
        {
            // Validate peer information
            if (!remotePeer.Validate())
            {
                _logger.LogWarning("Invalid peer information for hole punching");
                return false;
            }

            // Step 1: Get our external IP address
            var externalIp = await GetExternalIpAsync(ct).ConfigureAwait(false);
            
            if (externalIp == null)
            {
                _logger.LogWarning("Cannot perform hole punching without knowing external IP");
                return false;
            }

            _logger.LogDebug("Our external IP: {ExternalIp}", externalIp);

            // Step 2: Create UDP client for hole punching
            using var udpClient = new UdpClient();
            
            // Bind to a specific port to ensure consistent NAT mapping
            var localPort = 0; // Let OS assign a port
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
            
            var localEndpoint = udpClient.Client.LocalEndPoint as IPEndPoint;
            _logger.LogDebug("Local UDP endpoint: {LocalEndpoint}", localEndpoint);

            // Step 3: Perform simultaneous open
            // In a real implementation, this would be coordinated through a rendezvous server
            // Both peers would exchange their external endpoints and attempt to send simultaneously
            
            var remoteEndpoint = new IPEndPoint(remotePeer.IpAddress, remotePeer.Port);
            
            // Send initial packets to create NAT mapping
            var success = await PerformSimultaneousOpenAsync(
                udpClient, 
                remoteEndpoint, 
                ct).ConfigureAwait(false);

            if (success)
            {
                _logger.LogInformation(
                    "Successfully established UDP hole punch to {Peer}",
                    remotePeer.Endpoint);
                return true;
            }

            _logger.LogWarning(
                "UDP hole punching failed to {Peer}, relay fallback required",
                remotePeer.Endpoint);
            
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during UDP hole punching to {Peer}", remotePeer.Endpoint);
            return false;
        }
    }

    /// <summary>
    /// Performs the simultaneous open technique for UDP hole punching.
    /// </summary>
    /// <remarks>
    /// The simultaneous open technique works as follows:
    /// 
    /// 1. Both peers send UDP packets to each other's external addresses at the same time
    /// 2. These outgoing packets create temporary "holes" in the NAT mapping
    /// 3. When the packets arrive, they can traverse the NAT because a mapping exists
    /// 4. Subsequent packets can flow in both directions through these holes
    /// 
    /// This technique works with many (but not all) types of NAT:
    /// - Full Cone NAT: Works perfectly
    /// - Restricted Cone NAT: Works if timing is right
    /// - Port Restricted Cone NAT: Works if timing is right
    /// - Symmetric NAT: Usually fails (requires port prediction)
    /// 
    /// In a production system, this would be coordinated through a rendezvous server
    ///  /// that helh peers synchronize their simultaneous sends.
    /// </remarks>
    private async Task<bool> PerformSimultaneousOpenAsync(
        UdpClient udpClient,
        IPEndPoint remoteEndpoint,
        CancellationToken ct)
    {
        _logger.LogDebug("Starting simultaneous open to {RemoteEndpoint}", remoteEndpoint);

        try
        {
            // Create a unique handshake message
            var handshakeMessage = CreateHolePunchHandshake();
            
            // Send multiple packets to increase chances of success
            // This compensates for packet loss and timing issues
            const int maxAttempts = 5;
            const int attemptDelayMs = 200;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                _logger.LogDebug(
                    "Sending hole punch packet {Attempt}/{MaxAttempts} to {RemoteEndpoint}",
                    attempt + 1, maxAttempts, remoteEndpoint);

                // Send handshake packet
                await udpClient.SendAsync(
                    handshakeMessage, 
                    handshakeMessage.Length, 
                    remoteEndpoint).ConfigureAwait(false);

                // Try to receive a response
                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(attemptDelayMs, ct);
                var completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == receiveTask)
                {
                    var result = await receiveTask.ConfigureAwait(false);
                    
                    // Verify the response is a valid handshake
                    if (IsValidHolePunchResponse(result.Buffer))
                    {
                        _logger.LogInformation(
                            "Received valid hole punch response from {RemoteEndpoint}",
                            result.RemoteEndPoint);

                        // Send confirmation
                        var confirmMessage = CreateHolePunchConfirmation();
                        await udpClient.SendAsync(
                            confirmMessage,
                            confirmMessage.Length,
                            remoteEndpoint).ConfigureAwait(false);

                        return true;
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Received invalid hole punch response from {RemoteEndpoint}",
                            result.RemoteEndPoint);
                    }
                }
            }

            _logger.LogWarning(
                "Simultaneous open failed after {MaxAttempts} attempts to {RemoteEndpoint}",
                maxAttempts, remoteEndpoint);
            
            return false;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(
                ex,
                "Socket error during simultaneous open to {RemoteEndpoint}",
                remoteEndpoint);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Error during simultaneous open to {RemoteEndpoint}",
                remoteEndpoint);
            return false;
        }
    }

    /// <summary>
    /// Creates a UDP hole punch handshake message.
    /// </summary>
    /// <remarks>
    /// Format: [4 bytes: magic][4 bytes: timestamp][1 byte: message type]
    /// 
    /// Magic: 0x484F4C45 ("HOLE" in ASCII)
    /// Timestamp: Unix timestamp in seconds
    /// Message Type: 0x01 for handshake
    /// </remarks>
    private byte[] CreateHolePunchHandshake()
    {
        var message = new byte[9];
        
        // Magic number: "HOLE" in ASCII
        message[0] = 0x48; // 'H'
        message[1] = 0x4F; // 'O'
        message[2] = 0x4C; // 'L'
        message[3] = 0x45; // 'E'
        
        // Timestamp (4 bytes)
        var timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampBytes = BitConverter.GetBytes(timestamp);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(timestampBytes);
        Buffer.BlockCopy(timestampBytes, 0, message, 4, 4);
        
        // Message type: 0x01 for handshake
        message[8] = 0x01;
        
        return message;
    }

    /// <summary>
    /// Creates a UDP hole punch confirmation message.
    /// </summary>
    private byte[] CreateHolePunchConfirmation()
    {
        var message = new byte[9];
        
        // Magic number: "HOLE" in ASCII
        message[0] = 0x48; // 'H'
        message[1] = 0x4F; // 'O'
        message[2] = 0x4C; // 'L'
        message[3] = 0x45; // 'E'
        
        // Timestamp (4 bytes)
        var timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampBytes = BitConverter.GetBytes(timestamp);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(timestampBytes);
        Buffer.BlockCopy(timestampBytes, 0, message, 4, 4);
        
        // Message type: 0x02 for confirmation
        message[8] = 0x02;
        
        return message;
    }

    /// <summary>
    /// Validates a hole punch response message.
    /// </summary>
    private bool IsValidHolePunchResponse(byte[] message)
    {
        try
        {
            // Check minimum length
            if (message.Length < 9)
            {
                _logger.LogDebug("Hole punch response too short: {Length} bytes", message.Length);
                return false;
            }

            // Verify magic number
            if (message[0] != 0x48 || message[1] != 0x4F || 
                message[2] != 0x4C || message[3] != 0x45)
            {
                _logger.LogDebug("Invalid magic number in hole punch response");
                return false;
            }

            // Extract timestamp
            var timestampBytes = new byte[4];
            Buffer.BlockCopy(message, 4, timestampBytes, 0, 4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(timestampBytes);
            var timestamp = BitConverter.ToUInt32(timestampBytes, 0);

            // Verify timestamp is recent (within 30 seconds)
            var currentTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timeDiff = Math.Abs((int)(currentTimestamp - timestamp));
            
            if (timeDiff > 30)
            {
                _logger.LogDebug(
                    "Hole punch response timestamp too old: {TimeDiff} seconds",
                    timeDiff);
                return false;
            }

            // Check message type (should be handshake or confirmation)
            var messageType = message[8];
            if (messageType != 0x01 && messageType != 0x02)
            {
                _logger.LogDebug(
                    "Invalid message type in hole punch response: 0x{MessageType:X2}",
                    messageType);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error validating hole punch response");
            return false;
        }
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

    #region UPnP Implementation

    private const string SsdpMulticastAddress = "239.255.255.250";
    private const int SsdpPort = 1900;
    private const string SsdpSearchTarget = "urn:schemas-upnp-org:device:InternetGatewayDevice:1";
    private const string UpnpServiceType = "urn:schemas-upnp-org:service:WANIPConnection:1";

    /// <summary>
    /// Discovers UPnP-enabled gateway using SSDP (Simple Service Discovery Protocol).
    /// </summary>
    private async Task<string?> DiscoverUpnpGatewayAsync(CancellationToken ct)
    {
        _logger.LogDebug("Starting SSDP discovery for UPnP gateway");

        try
        {
            using var udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = 3000;
            udpClient.Client.SendTimeout = 3000;

            // SSDP M-SEARCH request
            var searchMessage = 
                "M-SEARCH * HTTP/1.1\r\n" +
                $"HOST: {SsdpMulticastAddress}:{SsdpPort}\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 2\r\n" +
                $"ST: {SsdpSearchTarget}\r\n" +
                "\r\n";

            var searchBytes = Encoding.ASCII.GetBytes(searchMessage);
            var multicastEndpoint = new IPEndPoint(IPAddress.Parse(SsdpMulticastAddress), SsdpPort);

            _logger.LogDebug("Sending SSDP M-SEARCH to {Endpoint}", multicastEndpoint);
            await udpClient.SendAsync(searchBytes, searchBytes.Length, multicastEndpoint).ConfigureAwait(false);

            // Wait for response
            var receiveTask = udpClient.ReceiveAsync();
            var timeoutTask = Task.Delay(3000, ct);
            var completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                _logger.LogDebug("SSDP discovery timed out");
                return null;
            }

            var result = await receiveTask.ConfigureAwait(false);
            var response = Encoding.ASCII.GetString(result.Buffer);

            _logger.LogDebug("Received SSDP response from {Endpoint}", result.RemoteEndPoint);

            // Extract LOCATION header
            var locationMatch = Regex.Match(response, @"LOCATION:\s*(.+)", RegexOptions.IgnoreCase);
            if (locationMatch.Success)
            {
                var location = locationMatch.Groups[1].Value.Trim();
                _logger.LogDebug("Found device location: {Location}", location);
                return location;
            }

            _logger.LogWarning("No LOCATION header found in SSDP response");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error during SSDP discovery");
            return null;
        }
    }

    /// <summary>
    /// Retrieves the control URL from the UPnP device description.
    /// </summary>
    private async Task<string?> GetControlUrlAsync(string deviceUrl, CancellationToken ct)
    {
        _logger.LogDebug("Fetching UPnP device description from {Url}", deviceUrl);

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await httpClient.GetStringAsync(deviceUrl, ct).ConfigureAwait(false);

            _logger.LogDebug("Parsing device description XML");

            // Parse XML to find WANIPConnection service
            var doc = XDocument.Parse(response);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            // Find the WANIPConnection service
            var service = doc.Descendants(ns + "service")
                .FirstOrDefault(s => 
                    s.Element(ns + "serviceType")?.Value == UpnpServiceType);

            if (service == null)
            {
                _logger.LogWarning("WANIPConnection service not found in device description");
                return null;
            }

            var controlUrl = service.Element(ns + "controlURL")?.Value;
            
            if (string.IsNullOrEmpty(controlUrl))
            {
                _logger.LogWarning("Control URL not found in service description");
                return null;
            }

            // Make control URL absolute if it's relative
            if (!controlUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var baseUri = new Uri(deviceUrl);
                var baseUrl = $"{baseUri.Scheme}://{baseUri.Host}:{baseUri.Port}";
                
                if (!controlUrl.StartsWith("/"))
                    controlUrl = "/" + controlUrl;
                    
                controlUrl = baseUrl + controlUrl;
            }

            return controlUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error getting control URL from device description");
            return null;
        }
    }

    /// <summary>
    /// Gets the local IP address used for outbound connections.
    /// </summary>
    private string? GetLocalIpAddress()
    {
        try
        {
            // Connect to a public IP to determine which local interface is used
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            
            var localEndPoint = socket.LocalEndPoint as IPEndPoint;
            return localEndPoint?.Address.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error determining local IP address");
            return null;
        }
    }

    /// <summary>
    /// Adds a port mapping via UPnP SOAP request.
    /// </summary>
    private async Task<bool> AddPortMappingAsync(
        string controlUrl, 
        int externalPort, 
        int internalPort, 
        string internalClient, 
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Sending AddPortMapping request: external={ExternalPort}, internal={InternalClient}:{InternalPort}",
            externalPort, internalClient, internalPort);

        try
        {
            // SOAP request for AddPortMapping
            var soapAction = "urn:schemas-upnp-org:service:WANIPConnection:1#AddPortMapping";
            var soapBody = 
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                "<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{externalPort}</NewExternalPort>" +
                "<NewProtocol>TCP</NewProtocol>" +
                $"<NewInternalPort>{internalPort}</NewInternalPort>" +
                $"<NewInternalClient>{internalClient}</NewInternalClient>" +
                "<NewEnabled>1</NewEnabled>" +
                "<NewPortMappingDescription>Dotp2pNet P2P</NewPortMappingDescription>" +
                "<NewLeaseDuration>0</NewLeaseDuration>" +
                "</u:AddPortMapping>" +
                "</s:Body>" +
                "</s:Envelope>";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var content = new StringContent(soapBody, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", $"\"{soapAction}\"");

            var response = await httpClient.PostAsync(controlUrl, content, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("UPnP port mapping added successfully");
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning(
                "UPnP AddPortMapping failed with status {StatusCode}: {Response}",
                response.StatusCode, responseBody);
            
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error sending AddPortMapping request");
            return false;
        }
    }

    /// <summary>
    /// Deletes a port mapping via UPnP SOAP request.
    /// Should be called on application shutdown to clean up port mappings.
    /// </summary>
    public async Task<bool> DeletePortMappingAsync(int externalPort, CancellationToken ct = default)
    {
        _logger.LogInformation("Attempting to delete UPnP port mapping for port {ExternalPort}", externalPort);

        try
        {
            // Step 1: Discover UPnP-enabled router
            var gatewayUrl = await DiscoverUpnpGatewayAsync(ct).ConfigureAwait(false);
            
            if (gatewayUrl == null)
            {
                _logger.LogWarning("No UPnP-enabled router discovered for cleanup");
                return false;
            }

            // Step 2: Get the control URL
            var controlUrl = await GetControlUrlAsync(gatewayUrl, ct).ConfigureAwait(false);
            
            if (controlUrl == null)
            {
                _logger.LogWarning("Failed to get UPnP control URL for cleanup");
                return false;
            }

            // Step 3: Delete port mapping
            var soapAction = "urn:schemas-upnp-org:service:WANIPConnection:1#DeletePortMapping";
            var soapBody = 
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                "<u:DeletePortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{externalPort}</NewExternalPort>" +
                "<NewProtocol>TCP</NewProtocol>" +
                "</u:DeletePortMapping>" +
                "</s:Body>" +
                "</s:Envelope>";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var content = new StringContent(soapBody, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", $"\"{soapAction}\"");

            var response = await httpClient.PostAsync(controlUrl, content, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("UPnP port mapping deleted successfully for port {ExternalPort}", externalPort);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning(
                "UPnP DeletePortMapping failed with status {StatusCode}: {Response}",
                response.StatusCode, responseBody);
            
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error deleting UPnP port mapping");
            return false;
        }
    }

    #endregion
}
