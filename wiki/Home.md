# Welcome to the Quark Wiki!

Quark is a high-performance, ultra-lightweight distributed actor framework for .NET 10+, built specifically for the Native AOT era. It achieves **100% reflection-free** operation through compile-time source generation.

## 🚀 Quick Links

### Getting Started
- **[Getting Started](Getting-Started)** - Install, setup, and create your first actor
- **[Examples](Examples)** - Code samples and common patterns

### Core Concepts
- **[Actor Model](Actor-Model)** - Understanding actors, lifecycle, and message processing
- **[Supervision](Supervision)** - Parent-child hierarchies and fault tolerance
- **[Persistence](Persistence)** - State management and storage
- **[Timers and Reminders](Timers-and-Reminders)** - Scheduling and temporal services

### Advanced Features
- **[Streaming](Streaming)** - Reactive streams and pub/sub patterns
- **[Clustering](Clustering)** - Distributed actors and cluster membership
- **[Source Generators](Source-Generators)** - AOT compatibility and code generation

### Reference
- **[API Reference](API-Reference)** - Key interfaces and classes
- **[FAQ](FAQ)** - Frequently asked questions and troubleshooting
- **[Contributing](Contributing)** - How to contribute to Quark

## ✨ Key Features

- 🚫 **Zero Reflection** - 100% reflection-free, all code generated at compile-time
- ✨ **Native AOT Ready** - Full support for .NET Native AOT compilation
- 🚀 **High Performance** - Lock-free messaging, persistent gRPC streams
- 🏗️ **Orleans-inspired** - Familiar virtual actor model with modern AOT support
- 🌐 **Distributed** - Redis clustering with consistent hashing
- 🔧 **Source Generation** - Actor factories, JSON serialization, logging
- ⚡ **Parallel Build** - Multi-project structure optimized for parallel compilation
- 🎯 **.NET 10 Target** - Built for the latest .NET platform

## 📚 Architecture Overview

Quark follows a clean, modular architecture:

```
┌─────────────────────────────────────────────────────────┐
│                     Your Application                     │
│                    (Business Logic)                      │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│                    Quark.Hosting                        │
│                  (Silo Management)                       │
└─────────────────────────────────────────────────────────┘
                           ↓
┌──────────────┬──────────────┬──────────────┬────────────┐
│  Quark.Core  │   Streaming  │  Clustering  │  Transport │
│   (Actors)   │  (Pub/Sub)   │   (Redis)    │   (gRPC)   │
└──────────────┴──────────────┴──────────────┴────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│              Quark.Abstractions (Interfaces)             │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│         Quark.Generators (Source Generation)             │
└─────────────────────────────────────────────────────────┘
```

## 🎯 Use Cases

Quark is ideal for:

- **Microservices** - Distributed business logic with actor isolation
- **IoT Applications** - Device management and state tracking
- **Game Servers** - Player sessions and game state management
- **Real-time Systems** - Event processing and streaming
- **Financial Systems** - Transaction processing and account management
- **Edge Computing** - Lightweight, AOT-compiled applications

## 🧪 Current Status

✅ **Phases 1-5 Complete:**
- Core actor runtime with lifecycle management
- Supervision hierarchies and fault tolerance
- Clustering and distributed actors (Redis + gRPC)
- State persistence with multiple backends
- Reactive streaming with implicit subscriptions
- **182/182 tests passing**

🚧 **In Progress:**
- Silo hosting and client gateway (Phase 6)
- Production hardening and observability (Phase 7)

## 📖 Documentation Structure

This wiki is organized into several sections:

1. **Getting Started** - Quick setup and your first actor
2. **Core Concepts** - Fundamental building blocks
3. **Advanced Features** - Distributed systems capabilities
4. **Developer Guide** - Architecture and internals
5. **Reference** - API docs and troubleshooting

## 🤝 Community

- **GitHub**: [thnak/Quark](https://github.com/thnak/Quark)
- **Issues**: [Report bugs or request features](https://github.com/thnak/Quark/issues)
- **Discussions**: [Ask questions and share ideas](https://github.com/thnak/Quark/discussions)

## 📄 License

Quark is open source under the [MIT License](https://github.com/thnak/Quark/blob/main/LICENSE).

---

Ready to get started? Head over to **[Getting Started](Getting-Started)** to create your first actor!
