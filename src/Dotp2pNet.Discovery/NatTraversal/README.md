# NAT Traversal

## Overview

NAT (Network Address Translation) traversal is one of the most challenging aspects of peer-to-peer networking. Most home and office networks use NAT, which allows multiple devices to share a single public IP address. While NAT provides security and conserves IPv4 addresses, it makes direct peer-to-peer connections difficult.

## The NAT Problem

### What is NAT?

When you connect to the internet from home, your router typically:
1. Assigns your device a private IP address (e.g., 192.168.1.100)
2. Translates outgoing packets to use the router's public IP address
3. Maintains a mapping table to route responses back to your device

### Why NAT Breaks P2P

```
Peer A (behind NAT)          Internet          Peer B (behind NAT)
192.168.1.100:6881    <--->  Router A  <--->  Router B  <--->  192.168.1.50:6881
                              1.2.3.4          5.6.7.8
```

Problems:
- Peer A doesn't know its public IP (1.2.3.4)
- Peer B can't directly connect to 192.168.1.100 (it's a private address)
- Router A blocks unsolicited incoming connections to protect the network

## NAT Traversal Techniques

### 1. STUN (Session Traversal Utilities for NAT)

**Purpose**: Discover your external IP address and port

**How it works**:
1. Send a UDP packet to a public STUN server
2. Server responds with the source IP:port it observed
3. This reveals your external address after NAT translation

**Example**:
```
You (192.168.1.100:6881) -> Router (NAT) -> STUN Server (stun.l.google.com:19302)
                                            |
                                            v
                            Response: "I see you as 1.2.3.4:54321"
```

**STUN Message Format** (RFC 5389):
```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|0 0|     STUN Message Type     |         Message Length        |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                         Magic Cookie                          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|                     Transaction ID (96 bits)                  |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                          Attributes                           |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

**Message Types**:
- `0x0001`: Binding Request
- `0x0101`: Binding Success Response
- `0x0111`: Binding Error Response

**Attributes**:
- `MAPPED-ADDRESS (0x0001)`: Your external IP:port (legacy)
- `XOR-MAPPED-ADDRESS (0x0020)`: Your external IP:port (XOR'd with magic cookie)

**Limitations**:
- Only discovers your address; doesn't establish connections
- Requires a public STUN server
- Doesn't work with symmetric NAT

### 2. UPnP (Universal Plug and Play)

**Purpose**: Automatically configure port forwarding on your router

**How it works**:
1. Discover UPnP-enabled router using SSDP (Simple Service Discovery Protocol)
2. Send AddPortMapping request: "Forward external port 6881 to my internal IP:6881"
3. Router creates the mapping
4. Now external peers can connect directly to you

**Example**:
```
Before UPnP:
External -> Router -> ❌ Blocked

After UPnP:
External:6881 -> Router -> Your Device:6881 ✅
```

**Advantages**:
- Fully automatic, no user configuration needed
- Works with most NAT types
- Enables true direct connections

**Limitations**:
- Not all routers support UPnP
- Some users disable UPnP for security reasons
- Requires router on local network

### 3. UDP Hole Punching

**Purpose**: Establish direct connections through NAT without router configuration

**How it works**:
1. Both peers discover their external addresses via STUN
2. Both peers exchange addresses through a third party (tracker/DHT)
3. Both peers simultaneously send UDP packets to each other's external address
4. These packets create temporary "holes" in the NAT mapping
5. Subsequent packets can traverse the NAT in both directions

**Example**:
```
Peer A                    Router A         Router B                    Peer B
192.168.1.100:6881       1.2.3.4          5.6.7.8                     10.0.0.50:6881
      |                     |                |                              |
      |-- Send to 5.6.7.8 ->|                |                              |
      |                     |-- Packet ----->|-- ❌ Blocked (no mapping)    |
      |                     |                |<-- Send to 1.2.3.4 ---------|
      |<-- ❌ Blocked -------|<-- Packet -----|                              |
      |                     |                |                              |
      |-- Send again ------>|                |                              |
      |                     |-- Packet ----->|-- ✅ Accepted (hole exists) ->|
      |<-- ✅ Accepted ------|<-- Packet -----|<-- Send again --------------|
      |                     |                |                              |
      |<========== Direct P2P Connection Established ======================>|
```

**NAT Types and Success Rate**:
- **Full Cone NAT**: 100% success (any external host can use the mapping)
- **Restricted Cone NAT**: 100% success (mapping allows specific external IP)
- **Port Restricted Cone NAT**: 100% success (mapping allows specific IP:port)
- **Symmetric NAT**: 0% success (different mapping for each destination)

**Limitations**:
- Doesn't work with symmetric NAT
- Requires precise timing coordination
- May require multiple attempts
- Firewall rules can still block

### 4. Relay/TURN (Traversal Using Relays around NAT)

**Purpose**: Fallback when direct connection is impossible

**How it works**:
1. Both peers connect to a relay server
2. Relay forwards data between peers
3. Slower and more expensive, but always works

**Example**:
```
Peer A <---> Relay Server <---> Peer B
```

**When to use**:
- Symmetric NAT on both sides
- Hole punching failed
- Firewall blocks UDP
- As last resort

## Implementation Strategy

### Phase 1: STUN (Current Task)
- Implement STUN client
- Discover external IP address
- Test with multiple STUN servers
- Handle failures gracefully

### Phase 2: UPnP (Task 19) ✅ Completed
- ✅ Implement SSDP discovery
- ✅ Send UPnP AddPortMapping requests
- ✅ Handle routers without UPnP
- ✅ Clean up mappings on shutdown

**Implementation Details**:

The UPnP implementation follows a multi-step process:

1. **SSDP Discovery**: Multicast M-SEARCH to 239.255.255.250:1900
   ```
   M-SEARCH * HTTP/1.1
   HOST: 239.255.255.250:1900
   MAN: "ssdp:discover"
   MX: 2
   ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1
   ```

2. **Device Description**: Parse XML from LOCATION header
   - Extract WANIPConnection service control URL
   - Handle both absolute and relative URLs

3. **Port Mapping**: SOAP AddPortMapping request
   ```xml
   <u:AddPortMapping xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1">
     <NewExternalPort>6881</NewExternalPort>
     <NewProtocol>TCP</NewProtocol>
     <NewInternalPort>6881</NewInternalPort>
     <NewInternalClient>192.168.1.100</NewInternalClient>
     <NewEnabled>1</NewEnabled>
     <NewPortMappingDescription>Dotp2pNet P2P</NewPortMappingDescription>
     <NewLeaseDuration>0</NewLeaseDuration>
   </u:AddPortMapping>
   ```

4. **Cleanup**: DeletePortMapping on shutdown
   - Removes forwarding rule from router
   - Prevents port conflicts on restart

**Usage**:
```csharp
var natTraversal = new NatTraversal(logger);

// Add port mapping
var success = await natTraversal.TryUpnpPortMappingAsync(6881, 6881);
if (success)
{
    Console.WriteLine("Port forwarding configured automatically");
}

// Clean up on shutdown
await natTraversal.DeletePortMappingAsync(6881);
```

### Phase 3: UDP Hole Punching (Task 20)
- Coordinate with remote peer
- Implement simultaneous send
- Retry with exponential backoff
- Fall back to relay if needed

## Testing NAT Traversal

### Local Testing
```bash
# Test STUN discovery
dotp2p> debug on
dotp2p> nat-info

Expected output:
External IP: 1.2.3.4 (discovered via STUN)
NAT Type: Port Restricted Cone
UPnP Available: Yes
```

### Network Scenarios

**Scenario 1: Both peers have public IPs**
- No NAT traversal needed
- Direct connection works immediately

**Scenario 2: One peer behind NAT**
- Peer behind NAT uses STUN to discover external IP
- Public peer connects directly
- Works with any NAT type

**Scenario 3: Both peers behind different NATs**
- Both use STUN to discover external IPs
- Try UPnP first (if available)
- Fall back to UDP hole punching
- Last resort: relay

**Scenario 4: Both peers behind same NAT**
- Detect via matching external IP
- Connect using internal addresses
- Much faster than going through external network

## Common Issues and Solutions

### Issue: STUN query times out
**Causes**:
- Firewall blocking UDP
- STUN server down
- Network connectivity issues

**Solutions**:
- Try multiple STUN servers
- Check firewall settings
- Verify internet connection

### Issue: UPnP not working
**Causes**:
- Router doesn't support UPnP
- UPnP disabled in router settings
- Security software blocking

**Solutions**:
- Enable UPnP in router settings
- Try manual port forwarding
- Fall back to hole punching

### Issue: Hole punching fails
**Causes**:
- Symmetric NAT
- Timing issues
- Firewall rules

**Solutions**:
- Retry with better timing
- Try different ports
- Fall back to relay

## Security Considerations

### STUN Security
- STUN requests are unauthenticated
- Use well-known public STUN servers
- Validate response format carefully
- Don't trust STUN for authentication

### UPnP Security
- UPnP has known security vulnerabilities
- Some users disable it intentionally
- Always clean up port mappings
- Don't rely on UPnP alone

### Hole Punching Security
- Validate peer addresses
- Use encryption for data transfer
- Implement rate limiting
- Detect and block malicious peers

## References

### RFCs
- [RFC 5389](https://tools.ietf.org/html/rfc5389) - STUN Protocol
- [RFC 5766](https://tools.ietf.org/html/rfc5766) - TURN Protocol
- [RFC 6886](https://tools.ietf.org/html/rfc6886) - NAT-PMP (alternative to UPnP)

### Papers
- "Peer-to-Peer Communication Across Network Address Translators" (2005)
- "NAT Traversal Techniques and Peer-to-Peer Applications" (2008)

### Tools
- [STUN Server List](https://gist.github.com/mondain/b0ec1cf5f60ae726202e)
- [NAT Type Detection Tool](https://www.stunprotocol.org/)
- Wireshark for packet analysis

## Learning Experiments

### Experiment 1: Discover Your External IP
```csharp
var natTraversal = new NatTraversal(logger);
var externalIp = await natTraversal.GetExternalIpAsync();
Console.WriteLine($"External IP: {externalIp}");
```

### Experiment 2: Compare Internal vs External
```csharp
var internalIp = Dns.GetHostEntry(Dns.GetHostName())
    .AddressList
    .First(ip => ip.AddressFamily == AddressFamily.InterNetwork);
var externalIp = await natTraversal.GetExternalIpAsync();

Console.WriteLine($"Internal: {internalIp}");
Console.WriteLine($"External: {externalIp}");
Console.WriteLine($"Behind NAT: {!internalIp.Equals(externalIp)}");
```

### Experiment 3: Test Multiple STUN Servers
```csharp
var servers = new[] { 
    "stun.l.google.com:19302",
    "stun1.l.google.com:19302",
    "stun2.l.google.com:19302"
};

foreach (var server in servers)
{
    var sw = Stopwatch.StartNew();
    var ip = await QueryStunServer(server);
    sw.Stop();
    Console.WriteLine($"{server}: {ip} ({sw.ElapsedMilliseconds}ms)");
}
```

### Experiment 4: Packet Analysis
Use Wireshark to capture STUN traffic:
1. Start Wireshark capture
2. Filter: `udp.port == 19302`
3. Run STUN query
4. Examine STUN request/response packets
5. Observe the XOR-MAPPED-ADDRESS attribute

## Future Enhancements

- IPv6 support (no NAT needed!)
- ICE (Interactive Connectivity Establishment) - combines STUN, TURN, and hole punching
- NAT type detection (cone vs symmetric)
- Automatic relay selection
- Connection quality metrics
