# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development Commands

### Building the Solution
```bash
# Build from the src directory
cd src
dotnet build

# Build in Release mode
dotnet build -c Release
```

### Running Tests
```bash
# Run all tests
dotnet test

# Run tests with specific filter (use class name, not fully qualified name)
dotnet test --filter "ClassName=ConcurrentPriorityQueueTests"

# Run benchmarks
cd src/DataFerry.Tests
dotnet run -c Release
```

### Creating NuGet Package
```bash
cd src/DataFerry
dotnet pack -c Release
```

## Project Architecture

This is a .NET 9.0 library providing high-performance concurrent data structures and utilities for distributed systems programming.

### Key Components

1. **Concurrent Data Structures** (`src/DataFerry/Concurrency/`)
   - `ConcurrentPriorityQueue`: Thread-safe priority queue implementation
   - `ConcurrentUnorderedLinkedList`: Lock-free linked list
   - `TaskOrchestrator`: Manages background tasks with dedicated threads

2. **Memory Management** (`src/DataFerry/Memory/`)
   - `PooledBufferWriter`: Efficient buffer management using array pools

3. **Serialization** (`src/DataFerry/Serialization/`)
   - `DataFerrySerializer`: High-performance serialization with buffer pooling

4. **Utilities** (`src/DataFerry/Utilities/`)
   - `HashGenerator`: MurmurHash3 implementation
   - `PollyPolicyGenerator`: Creates resilience policies for distributed operations

### Design Patterns

- **Interface-First Design**: All major components have corresponding interfaces in `Contracts` folders
- **Dependency Injection Ready**: Components are designed for DI with constructor injection
- **Array Pooling**: Extensive use of `ArrayPool<T>` for memory efficiency
- **Async/Await**: Task-based asynchronous patterns throughout

### Future Components (Planned for 2.0.0+)

The codebase is preparing for a distributed caching implementation based on W-TinyLFU algorithm:
- Window TinyLFU admission policy
- Segmented LRU (SLRU) with probation and protected segments
- Count-Min Sketch for frequency estimation
- Background task orchestration for cache maintenance

### Code Quality

The project uses several analyzers:
- StyleCop.Analyzers for code style consistency
- Microsoft.CodeAnalysis.Analyzers
- Microsoft.EntityFrameworkCore.Analyzers
- Microsoft.VisualStudio.Threading.Analyzers

Common warnings to address:
- SA1633: Missing file headers
- SA1005/SA1512: Comment formatting
- CS8618: Non-nullable field warnings