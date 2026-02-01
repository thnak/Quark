# Awesome Pizza - Product Example

> A production-grade demo application showcasing the Quark Framework's distributed actor capabilities through a global pizza ordering and tracking system.

⚠️ **IMPORTANT ARCHITECTURE UPDATE**: This project now follows the correct distributed actor pattern:
- **Silos** = Central actor host (all actors live here)
- **Gateway** = Connects to actors in Silo (should use proxies)  
- **MQTT** = Integrated into Silo to update actors directly

📄 See **[ARCHITECTURE-FIX.md](ARCHITECTURE-FIX.md)** for complete details.

---

## 📖 Overview

**Awesome Pizza** is a comprehensive demonstration of the Quark Framework built for the Native AOT era. This project showcases how to build a real-time, globally distributed system using virtual actors, persistent state, reactive streaming, and IoT integration.

### What Makes This Special?

- **100% Native AOT** - Sub-50ms startup times, minimal memory footprint
- **Real-time Everything** - Live order tracking, driver GPS updates via MQTT
- **Production Patterns** - Supervision, persistence, clustering, observability
- **Realistic Use Case** - Actual pizza delivery business logic with edge cases
- **Educational** - Extensive documentation and step-by-step guides

---

## 🎯 Project Goals

1. **Showcase Quark's Power**: Demonstrate distributed actors, state management, streaming
2. **Prove AOT Benefits**: Highlight startup speed, memory efficiency, high density
3. **Production Ready**: Include monitoring, fault tolerance, scalability patterns
4. **Developer Friendly**: Clear documentation, examples, and learning path

---

## 📁 Repository Structure

```
productExample/
├── README.md (this file)           # Project overview
│
├── plans/                           # Planning and design documents
│   ├── 01-AWESOME-PIZZA-IMPLEMENTATION-PLAN.md
│   ├── 02-FEATURE-SPECIFICATIONS.md
│   └── 03-QUICK-START-GUIDE.md
│
├── src/                             # Source code (to be implemented)
│   ├── Quark.AwesomePizza.Shared/
│   ├── Quark.AwesomePizza.Silo/
│   ├── Quark.AwesomePizza.Gateway/
│   ├── Quark.AwesomePizza.MqttBridge/
│   └── Quark.AwesomePizza.Tests/
│
└── implements/                      # Implementation tracking
    ├── tasks/                       # Task breakdown and progress
    └── diagrams/                    # Architecture diagrams
```

---

## 🚀 Quick Start

Want to get started quickly? Follow our **[Quick Start Guide](plans/03-QUICK-START-GUIDE.md)** to:

1. Set up infrastructure (Redis, MQTT)
2. Create your first pizza order actor
3. Build a REST API gateway
4. Test end-to-end order flow

**Time to first order**: ~15 minutes ⚡

---

## 📚 Documentation

### Planning Documents

1. **[Implementation Plan](plans/01-AWESOME-PIZZA-IMPLEMENTATION-PLAN.md)**
   - Complete system architecture
   - Phase-by-phase roadmap (10 weeks)
   - Technology stack and deployment strategy
   - Success metrics and performance targets

2. **[Feature Specifications](plans/02-FEATURE-SPECIFICATIONS.md)**
   - Detailed technical specs for each feature
   - Actor interfaces and implementations
   - State models and data flows
   - Testing strategies

3. **[Quick Start Guide](plans/03-QUICK-START-GUIDE.md)**
   - Step-by-step setup instructions
   - Hello World example
   - Troubleshooting tips
   - Next steps and learning resources

---

## 🏗️ System Architecture

### High-Level Overview

```
┌──────────────┐
│  Mobile App  │  (Customer & Driver)
└──────┬───────┘
       │
┌──────▼───────┐
│   Gateway    │  ASP.NET Minimal API (Port 5000)
│   REST API   │
└──────┬───────┘
       │
       ├─────────────────┬─────────────────┐
       │                 │                 │
┌──────▼─────┐    ┌─────▼──────┐   ┌─────▼──────┐
│  Silo 1    │    │  Silo 2    │   │  Silo N    │
│  Actors    │◄──►│  Actors    │◄─►│  Actors    │
└──────┬─────┘    └─────┬──────┘   └─────┬──────┘
       │                 │                 │
       └─────────────────┴─────────────────┘
                         │
                   ┌─────▼─────┐
                   │   Redis   │  (Clustering + State)
                   └───────────┘
                         
       ┌─────────────────┐
       │  MQTT Broker    │  (IoT Telemetry)
       │  Port 1883      │
       └─────────────────┘
```

### Key Components

1. **Silos** - AOT-compiled console apps hosting actors
   - OrderActor, KitchenActor, DriverActor, ChefActor
   - Distributed across data centers
   - Auto-scaling based on load

2. **Gateway** - REST API for clients
   - Type-safe actor proxies
   - Server-Sent Events for real-time updates
   - Serves static web UI

3. **MQTT Bridge** - IoT integration
   - GPS tracking from driver devices
   - Kitchen equipment telemetry
   - Real-time location updates

4. **Redis** - Distributed infrastructure
   - Cluster membership (consistent hashing)
   - State persistence (optimistic concurrency)
   - Reminder storage

---

## 🎯 Key Features

### ✅ Order Lifecycle Management
- Complete pizza order workflow
- State machine with validation
- Persistent reminders for stuck orders
- Event streaming for real-time updates

### ✅ Real-time Driver Tracking
- MQTT-based GPS telemetry
- Sub-100ms location update latency
- ETA calculation and route optimization
- Live map visualization

### ✅ Kitchen Display System
- Real-time order queue
- Chef assignment and load balancing
- Supervision hierarchy (Restaurant → Kitchen → Chef)
- Cooking timers with callbacks

### ✅ Inventory Management
- Automatic stock depletion
- Low-stock alerts with reminders
- Reorder threshold configuration
- Historical inventory reports

### ✅ Manager Dashboard
- Multi-stream aggregation
- Real-time metrics (SSE)
- Order management interface
- Performance analytics

---

## 📊 Performance Targets

| Metric | Target | Why It Matters |
|--------|--------|----------------|
| Order Creation | < 50ms (p99) | Customer experience |
| Location Update | < 100ms (p99) | Real-time tracking |
| Startup Time | < 50ms | Native AOT advantage |
| Memory/Silo | < 50MB | High-density deployment |
| Throughput | 10,000+ orders/sec | Scalability |
| Concurrent Orders | 100,000+ | Global scale |

---

## 🛠️ Technology Stack

### Backend
- **.NET 10** with Native AOT
- **Quark Framework** for distributed actors
- **Redis** for clustering and state
- **MQTT.NET** for IoT integration
- **Serilog** for structured logging
- **Prometheus** for metrics

### Frontend
- **React/Vue/Svelte** (TBD based on preference)
- **TypeScript** for type safety
- **Leaflet/Mapbox** for maps
- **EventSource API** for SSE

### Infrastructure
- **Docker & Docker Compose**
- **Redis Cluster** (3-node HA)
- **Mosquitto MQTT Broker**
- **Nginx** (reverse proxy)

---

## 📅 Implementation Roadmap

### Phase 1: Foundation (Week 1-2) ✅
- [x] Create planning documents
- [x] Define system architecture
- [x] Write feature specifications
- [ ] Set up project structure
- [ ] Configure infrastructure (Docker)

### Phase 2: Order Lifecycle (Week 3-4)
- [ ] Implement OrderActor
- [ ] Implement KitchenActor and ChefActor
- [ ] Add state persistence
- [ ] Build gateway endpoints

### Phase 3: Delivery System (Week 5-6)
- [ ] Implement DriverActor
- [ ] Build MQTT bridge
- [ ] Add real-time tracking
- [ ] Create driver mobile simulation

### Phase 4: Inventory (Week 7)
- [ ] Implement InventoryActor
- [ ] Add persistent reminders
- [ ] Build inventory dashboard

### Phase 5: Manager Dashboard (Week 8)
- [ ] Implement RestaurantActor
- [ ] Build SSE streaming endpoint
- [ ] Create web UI components

### Phase 6: Production Hardening (Week 9-10)
- [ ] Performance optimization
- [ ] Reliability patterns
- [ ] Observability setup
- [ ] Documentation

**Current Status**: Phase 1 - Planning Complete ✅

---

## 🧪 Testing Strategy

### Unit Tests
- Actor behavior validation
- State transitions
- Error handling

### Integration Tests
- End-to-end order flow
- MQTT message routing
- State persistence

### Performance Tests
- Load testing (10K+ orders/sec)
- Latency benchmarks
- Memory profiling

### Stress Tests
- Silo failure recovery
- Network partition handling
- High concurrency scenarios

---

## 📖 Learning Path

### For Beginners
1. Read the [Quick Start Guide](plans/03-QUICK-START-GUIDE.md)
2. Build the Hello World example
3. Explore the [Quark documentation](../docs/)
4. Study the [Feature Specifications](plans/02-FEATURE-SPECIFICATIONS.md)

### For Advanced Users
1. Review the [Implementation Plan](plans/01-AWESOME-PIZZA-IMPLEMENTATION-PLAN.md)
2. Understand the supervision patterns
3. Explore state persistence strategies
4. Study the clustering architecture

### For Contributors
1. Pick a task from the roadmap
2. Follow the [Quark contribution guidelines](../docs/)
3. Write tests for your changes
4. Submit a pull request

---

## 🤝 Contributing

We welcome contributions! Here's how to get involved:

1. **Pick a Feature**: Choose from the [roadmap](#-implementation-roadmap)
2. **Create a Task**: Document your work in `implements/tasks/`
3. **Write Tests**: Follow existing test patterns
4. **Document**: Update relevant planning docs
5. **Submit PR**: Include benchmarks if applicable

---

## 📞 Support

### Getting Help

- **Documentation**: Start with `plans/` folder
- **Examples**: Check `../examples/` for working code
- **Issues**: Open an issue on GitHub
- **Discussions**: Join the Quark community

### Common Issues

See the [Quick Start Guide - Troubleshooting](plans/03-QUICK-START-GUIDE.md#-troubleshooting) section.

---

## 🏆 Success Metrics

We'll know this demo is successful when:

- ✅ **Development Speed**: Developers can create their first actor in < 30 minutes
- ✅ **Performance**: Meets all latency and throughput targets
- ✅ **Reliability**: 99.9% uptime with fault tolerance
- ✅ **Scalability**: Linear scaling to 100+ silos
- ✅ **Adoption**: Used as reference by other Quark projects

---

## 📜 License

This project is part of the Quark Framework and follows the same MIT License.

---

## 🙏 Acknowledgments

- **Quark Framework Team** - For building an amazing actor framework
- **Microsoft Orleans** - Inspiration for virtual actor patterns
- **MQTT Community** - For IoT protocol standards
- **You** - For exploring this demo!

---

**Version**: 1.0.0  
**Status**: Planning Complete - Ready for Implementation  
**Last Updated**: 2026-01-31

---

## 🚀 Let's Build Something Awesome!

Ready to start? Head to the **[Quick Start Guide](plans/03-QUICK-START-GUIDE.md)** and create your first pizza order in 15 minutes!

Questions? Check the [Implementation Plan](plans/01-AWESOME-PIZZA-IMPLEMENTATION-PLAN.md) for detailed architecture and roadmap.

**Happy Coding! 🍕🎉**
