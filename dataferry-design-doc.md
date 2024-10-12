# DataFerry Caching Library: Design Document

This document outlines the design and responsibilities of the `DataFerry` caching library, focusing on the interaction between the `DataFerry` class (the orchestrator) and the `IDistributedCache` interface.

## 1. Introduction

`DataFerry` is a high-performance, tiered caching library designed to simplify and optimize caching in .NET applications. It employs a two-level caching strategy, combining an in-memory cache (L1) with a distributed cache (L2) to provide low-latency data access and scalability.

## 2. Components

*   **`DataFerry`:** The core orchestration class responsible for managing the caching workflow and interactions between the L1 and L2 caches.
*   **`IDistributedCache`:** An interface representing the distributed cache implementation (e.g., `RedisDistributedCache`).
*   **`IFastMemCache`:** An interface for the in-memory L1 cache.
*   **`IHybridCacheSerializer`:** An interface for serializing and deserializing objects to be stored in the caches.

## 3. Responsibilities

### 3.1 `DataFerry` (Orchestrator)

*   **Tiered Cache Management:**
    *   Handles the lookup and retrieval of cached data from both L1 and L2 caches.
    *   Manages the population and eviction of cache entries in both caches.
    *   Enforces cache coherency between the L1 and L2 caches.
*   **Cache Stampede Protection:**
    *   Implements a mechanism to prevent multiple requests from simultaneously hitting the underlying data source when a cache entry is missing. This is achieved by coordinating concurrent requests and ensuring that only one request triggers the data retrieval process while others wait for the result.
*   **Serialization:**
    *   Uses the `IHybridCacheSerializer` to serialize objects before storing them in the L2 cache and deserialize them when retrieving them.
*   **Data Source Interaction:**
    *   Provides a mechanism to abstract the interaction with the underlying data source (e.g., database, API) through delegates or interfaces. This allows for flexibility in how data is retrieved and populated into the cache.
    *   Uses a dedicated interface (e.g., `IDataSource<T>`) to encapsulate data retrieval logic.
    *   Ensures consistency between the cache and the database by utilizing the same `IDataSource<T>` for reading and writing data.
*   **Cache Entry Management:**
    *   Handles cache entry expiration and eviction policies.
    *   Provides methods for adding, updating, removing, and refreshing cache entries.
*   **Observability:**
    *   Integrates with OpenTelemetry to provide tracing and metrics for monitoring cache performance and behavior.

### 3.2 `IDistributedCache`

*   **Distributed Cache Interaction:**
    *   Provides an abstraction for interacting with the distributed cache (e.g., Redis).
    *   Handles the storage and retrieval of serialized data in the L2 cache.
    *   Manages the connection and configuration of the distributed cache.
*   **Efficient Data Handling:**
    *   Uses `IBufferWriter<byte>` and `ReadOnlySequence<byte>` to efficiently handle byte data, minimizing memory allocations and copies.
*   **Batch Operations:**
    *   Offers methods for performing batch operations (get, set, remove) on the cache for improved performance.

## 4. Interaction Flow

1.  When a request for data arrives, `DataFerry` first checks the L1 (in-memory) cache.
2.  If the data is not found in L1, `DataFerry` checks the L2 (distributed) cache.
3.  If the data is not found in either cache, `DataFerry` uses the provided `IDataSource<T>` to retrieve the data from the origin.
4.  The retrieved data is then serialized using the `IHybridCacheSerializer` and stored in both L1 and L2 caches.
5.  Subsequent requests for the same data will hit the cache, providing fast and efficient data access.

**4.1 Resiliency**

Integrate Polly to handle transient faults when interacting with the L2 cache (StackExchange.Redis). Allow developers to configure retry policies, circuit breakers, and other resiliency mechanisms.

**4.2 Cache-Database Synchronization**

Introduce an interface (e.g., `IDataSource<T>`) to abstract interactions with the data source. The `factory` delegate in `GetOrCreateAsync` can accept an instance of this interface, enabling developers to implement custom logic for retrieving data and ensuring consistency between the cache and the database.

## 5. Key Benefits

*   **High Performance:** The combination of L1 and L2 caches provides low-latency data access.
*   **Scalability:** The distributed L2 cache enables scaling the application across multiple servers.
*   **Resilience:** The L2 cache provides resilience in case of in-memory cache failures or server restarts.
*   **Efficiency:** The use of buffers and asynchronous operations minimizes memory allocations and improves performance.
*   **Simplicity:** `DataFerry` provides a clean and easy-to-use API for managing the caching workflow.
*   **Consistency:** The use of `IDataSource<T>` helps maintain consistency between the cache and the data source.
*   **Observability:** OpenTelemetry integration enables monitoring and tracing of cache operations.

**6.  Implementation Details**

* **.NET Standard Target:** Target .NET Standard 2.0 for compatibility with a wide range of .NET versions.
* **Dependency Injection:** Use a builder pattern to facilitate easy configuration and registration of the hybrid cache with dependency injection in ASP.NET Core applications.
* **Configuration:** Provide options for configuring L1 and L2 cache settings, serialization, and Polly policies.