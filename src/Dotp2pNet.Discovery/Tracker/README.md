# Tracker Client Implementation

## Overview

The tracker client implements the HTTP tracker protocol for centralized peer discovery in the BitTorrent network. Trackers maintain lists of peers participating in file sharing and coordinate peer discovery through announce requests.

## Key Concepts

### What is a Tracker?

A tracker is a centralized server that:
- Maintains a list of peers for each torrent (identified by info hash)
- Responds to announce requests with peer lists
- Tracks swarm statistics (number of seeders and leechers)
- Enforces announce intervals to prevent server overload

### The Announce Protocol

The announce protocol is a simple HTTP GET request with query parameters:

```
GET /announce?info_hash=%12%34...&peer_id=%AB%CD...&port=6881&uploaded=0&downloaded=0&left=1024&compact=1
```

**Required Parameters:**
- `info_hash`: 20-byte SHA-1 hash of the torrent (URL-encoded)
- `peer_id`: 20-byte unique identifier for this client (URL-encoded)
- `port`: Port number this client is listening on
- `uploaded`: Total bytes uploaded so far
- `downloaded`: Total bytes downloaded so far
- `left`: Bytes remaining to download (0 if seeding)

**Optional Parameters:**
- `event`: Lifecycle event (started, completed, stopped)
- `compact`: Request compact peer format (1 = yes, 0 = no)
- `numwant`: Number of peers desired (default 50)

### Tracker Response Format

Trackers respond with bencoded data:

```
d8:intervali1800e5:peers6:...12:complete i5e10:incompletei12ee
```

Decoded:
```json
{
  "interval": 1800,
  "peers": "...",
  "complete": 5,
  "incomplete": 12
}
```

**Response Fields:**
- `interval`: Seconds between regular announces (typically 1800 = 30 minutes)
- `min interval`: Minimum seconds between announces (optional)
- `tracker id`: Tracker-assigned ID for this client (optional)
- `complete`: Number of seeders (peers with complete file)
- `incomplete`: Number of leechers (peers downloading)
- `peers`: List of peers (see formats below)
- `warning message`: Optional warning from tracker
- `failure reason`: Error message if request failed

### Peer Formats

**Compact Format (Binary):**
Each peer is 6 bytes: 4 bytes IP + 2 bytes port (big-endian)

```
[192.168.1.50:6881] = [C0 A8 01 32 1A E1]
```

**Dictionary Format:**
List of dictionaries with peer information:

```
[
  {"peer id": "...", "ip": "192.168.1.50", "port": 6881},
  {"peer id": "...", "ip": "10.0.0.15", "port": 6882}
]
```

### Bencoding

Bencoding is a simple encoding format used by BitTorrent:

**Integers:** `i[number]e`
```
i42e = 42
i-3e = -3
```

**Strings:** `[length]:[string]`
```
4:spam = "spam"
0: = ""
```

**Lists:** `l[elements]e`
```
l4:spam4:eggse = ["spam", "eggs"]
li1ei2ei3ee = [1, 2, 3]
```

**Dictionaries:** `d[key][value]...e` (keys must be strings, sorted)
```
d3:cow3:moo4:spam4:eggse = {"cow": "moo", "spam": "eggs"}
```

## Implementation Details

### TrackerClient Class

The `TrackerClient` class implements `ITrackerClient` and provides:

1. **AnnounceAsync**: Full announce with all parameters
2. **GetPeersAsync**: Convenience method for getting peer list

### BencodeParser Class

The `BencodeParser` class provides:

1. **ParseDictionary**: Parse bencoded data into dictionary
2. **Helper methods**: GetString, GetBytes, GetInteger, GetList, GetDictionary

### Announce Interval Tracking

The client tracks announce intervals per tracker to avoid:
- Overloading tracker servers
- Getting banned for too-frequent announces
- Wasting bandwidth on unnecessary requests

**Interval Rules:**
- Lifecycle events (started, completed, stopped) are never throttled
- Regular announces respect the interval returned by tracker
- Default interval is 1800 seconds (30 minutes) if not specified

### URL Encoding

BitTorrent uses specific URL encoding rules:
- Alphanumeric characters and `.`, `-`, `_`, `~` are not encoded
- All other bytes are encoded as `%XX` (hex)

Example:
```
info_hash = [0x12, 0x34, 0x56, 0x78, 0x9A]
encoded = "%12%34%56%78%9A"
```

## Usage Example

```csharp
// Create tracker client
var httpClient = new HttpClient();
var logger = loggerFactory.CreateLogger<TrackerClient>();
var trackerClient = new TrackerClient(httpClient, logger);

// Generate peer ID (20 bytes)
byte[] peerId = new byte[20];
RandomNumberGenerator.Fill(peerId);

// Announce to tracker
var response = await trackerClient.AnnounceAsync(
    trackerUrl: "http://tracker.example.com:8080/announce",
    infoHash: torrentMetadata.InfoHash,
    peerId: peerId,
    port: 6881,
    downloaded: 0,
    uploaded: 0,
    left: torrentMetadata.TotalSize,
    eventType: TrackerEvent.Started,
    ct: cancellationToken);

Console.WriteLine($"Received {response.Peers.Count} peers");
Console.WriteLine($"Seeders: {response.Complete}, Leechers: {response.Incomplete}");
Console.WriteLine($"Next announce in {response.Interval} seconds");

// Connect to peers
foreach (var peer in response.Peers)
{
    Console.WriteLine($"Peer: {peer.Endpoint}");
    // Connect to peer...
}
```

## Learning Experiments

### Experiment 1: Observe Tracker Communication

Use Wireshark or Fiddler to capture tracker HTTP requests:

1. Start packet capture
2. Run announce request
3. Observe HTTP GET request with URL-encoded parameters
4. Observe bencoded response
5. Decode response manually to understand format

### Experiment 2: Test Different Events

Try announcing with different event types:

```csharp
// Started event
await trackerClient.AnnounceAsync(..., TrackerEvent.Started, ...);

// Regular announce (no event)
await trackerClient.AnnounceAsync(..., TrackerEvent.None, ...);

// Completed event
await trackerClient.AnnounceAsync(..., TrackerEvent.Completed, ...);

// Stopped event
await trackerClient.AnnounceAsync(..., TrackerEvent.Stopped, ...);
```

Observe how the tracker responds differently to each event.

### Experiment 3: Interval Enforcement

Try announcing multiple times rapidly:

```csharp
// First announce
var response1 = await trackerClient.AnnounceAsync(..., TrackerEvent.Started, ...);
Console.WriteLine($"Interval: {response1.Interval}");

// Immediate second announce (should be throttled)
try
{
    var response2 = await trackerClient.AnnounceAsync(..., TrackerEvent.None, ...);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Throttled: {ex.Message}");
}

// Wait for interval, then announce again
await Task.Delay(TimeSpan.FromSeconds(response1.Interval));
var response3 = await trackerClient.AnnounceAsync(..., TrackerEvent.None, ...);
Console.WriteLine("Announce succeeded after waiting");
```

### Experiment 4: Parse Bencoded Data

Practice parsing bencoded data manually:

```csharp
// Integer
byte[] data1 = Encoding.ASCII.GetBytes("i42e");
var dict1 = BencodeParser.ParseDictionary(data1); // Should fail - not a dict

// String
byte[] data2 = Encoding.ASCII.GetBytes("4:spam");
// Parse manually...

// Dictionary
byte[] data3 = Encoding.ASCII.GetBytes("d3:cow3:mooe");
var dict3 = BencodeParser.ParseDictionary(data3);
Console.WriteLine(BencodeParser.GetString(dict3, "cow")); // "moo"
```

## Common Issues and Solutions

### Issue: Tracker Returns "failure reason"

**Cause:** Invalid parameters or tracker-specific requirements

**Solution:**
- Verify info_hash is exactly 20 bytes
- Verify peer_id is exactly 20 bytes
- Check if tracker requires specific user-agent
- Ensure port is valid (1-65535)

### Issue: No Peers Returned

**Cause:** Torrent has no active peers, or tracker is empty

**Solution:**
- Check `complete` and `incomplete` counts in response
- Try DHT for peer discovery as fallback
- Verify info_hash matches the torrent

### Issue: Announce Throttled

**Cause:** Announcing too frequently

**Solution:**
- Respect the `interval` returned by tracker
- Use lifecycle events (started, completed, stopped) when appropriate
- Don't announce on every piece completion

### Issue: Invalid Bencoded Response

**Cause:** Tracker returned HTML error page or malformed data

**Solution:**
- Check HTTP status code (should be 200)
- Verify tracker URL is correct
- Check if tracker requires authentication
- Try different tracker from torrent's tracker list

## References

- [BEP 3: The BitTorrent Protocol Specification](http://www.bittorrent.org/beps/bep_0003.html)
- [BEP 23: Tracker Returns Compact Peer Lists](http://www.bittorrent.org/beps/bep_0023.html)
- [Bencoding Specification](https://wiki.theory.org/BitTorrentSpecification#Bencoding)
