# Dotp2pNet

A peer-to-peer file sharing system built in .NET, implementing the BitTorrent protocol for educational purposes.

## Overview

Dotp2pNet is a learning-focused implementation of a P2P file sharing system that demonstrates core networking concepts, distributed systems principles, and the BitTorrent protocol. This project is designed to help developers understand how P2P networks operate at a fundamental level.

## Architecture

The project follows a layered architecture with clear separation of concerns:

### Core Layer (`Dotp2pNet.Core`)
- **Interfaces**: Core domain interfaces (IMessageFramer, IPeerConnection, IConnectionManager, IPieceManager, IBitfield)
- **Configuration**: Application configuration models
- **Logging**: Serilog-based logging infrastructure
- **Dependency Injection**: Service registration and DI container setup

### Networking Layer (`Dotp2pNet.Networking`)
- TCP connection management
- Message framing and protocol handling
- Peer connection lifecycle management

### Storage Layer (`Dotp2pNet.Storage`)
- Piece storage and retrieval
- File assembly and verification
- Hash validation (SHA1)

### Discovery Layer (`Dotp2pNet.Discovery`)
- Tracker communication (HTTP/UDP)
- DHT (Distributed Hash Table) implementation
- Peer discovery mechanisms

### Orchestration Layer (`Dotp2pNet.Orchestration`)
- Download coordination
- Piece selection strategies
- Peer management and choking algorithm

### CLI (`Dotp2pNet.CLI`)
- Command-line interface
- User interaction and commands
- Application entry point

## Features

- ✅ Layered architecture with dependency injection
- ✅ Structured logging with Serilog
- ✅ Configuration management
- 🚧 TCP-based peer connections
- 🚧 BitTorrent protocol message framing
- 🚧 Piece-based file transfer
- 🚧 Tracker communication
- 🚧 DHT peer discovery
- 🚧 Download orchestration

## Getting Started

### Prerequisites

- .NET 10.0 SDK or later
- Basic understanding of networking concepts
- Familiarity with C# and async/await patterns

### Building the Project

```bash
# Clone the repository
git clone <repository-url>
cd DotP2PNet

# Build the solution
dotnet build

# Run the CLI
dotnet run --project src/Dotp2pNet.CLI
```

### Configuration

Configuration is managed through `appsettings.json` in the CLI project. Key settings include:

- **Network**: Listen port, max connections, DHT settings
- **Storage**: Download directory, piece size
- **Logging**: Log directory, minimum level
- **Peer**: Peer ID, request limits, choking settings

## Project Structure

```
DotP2PNet/
├── src/
│   ├── Dotp2pNet.Core/           # Core interfaces and infrastructure
│   ├── Dotp2pNet.Networking/     # Network communication
│   ├── Dotp2pNet.Storage/        # File storage and management
│   ├── Dotp2pNet.Discovery/      # Peer discovery
│   ├── Dotp2pNet.Orchestration/  # Download coordination
│   └── Dotp2pNet.CLI/            # Command-line interface
├── Dotp2pNet.sln
└── README.md
```

## Learning Goals

This project is designed to teach:

1. **Network Programming**: TCP sockets, message framing, protocol implementation
2. **Distributed Systems**: Peer discovery, distributed hash tables, consensus
3. **File Management**: Chunking, hashing, verification, assembly
4. **Concurrency**: Async/await, parallel downloads, connection pooling
5. **Software Architecture**: Layered design, dependency injection, SOLID principles

## Development Roadmap

### Phase 1: Foundation (Current)
- [x] Project structure and core interfaces
- [x] Dependency injection setup
- [x] Logging infrastructure
- [x] Configuration management
- [ ] Message framing implementation
- [ ] Basic peer connection

### Phase 2: Core Protocol
- [ ] BitTorrent handshake
- [ ] Message types (choke, unchoke, interested, have, bitfield, request, piece)
- [ ] Piece manager and bitfield
- [ ] Basic file download

### Phase 3: Discovery
- [ ] HTTP tracker communication
- [ ] UDP tracker protocol
- [ ] DHT implementation
- [ ] Peer exchange

### Phase 4: Optimization
- [ ] Piece selection strategies (rarest first, endgame)
- [ ] Choking algorithm
- [ ] Upload management
- [ ] Performance tuning

## Contributing

This is an educational project. Contributions that improve learning value are welcome:

- Clear code comments explaining "why" not just "what"
- Documentation of concepts and design decisions
- Examples and tutorials
- Bug fixes and improvements

## Resources

- [BitTorrent Protocol Specification (BEP 0003)](http://www.bittorrent.org/beps/bep_0003.html)
- [DHT Protocol (BEP 0005)](http://www.bittorrent.org/beps/bep_0005.html)
- [UDP Tracker Protocol (BEP 0015)](http://www.bittorrent.org/beps/bep_0015.html)

## License

This project is for educational purposes. See LICENSE file for details.

## Disclaimer

This implementation is for learning purposes only. For production use, consider established BitTorrent clients like qBittorrent, Transmission, or Deluge.
