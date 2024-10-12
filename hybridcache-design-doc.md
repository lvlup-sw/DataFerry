# HybridCache Implementation Design Document

**1. Introduction**

This document outlines the design for a robust and efficient hybrid caching solution for .NET applications, optimized for high-volume scenarios. The implementation will prioritize simplicity and ease of use while providing advanced features like resiliency, distributed caching, and cache-database synchronization.

**2. Goals**

* **Concrete Implementation:** Provide a concrete implementation of a hybrid caching strategy, drawing inspiration from .NET 9's `HybridCache` while ensuring compatibility with older .NET versions.
* **Tiered Caching:** Implement a two-tiered caching scheme:
    * **L1:**  A custom in-memory cache optimized for speed (potentially using `FastMemCache` or a custom `RCache` in the future).
    * **L2:**  `StackExchange.Redis` for distributed caching.
* **Stampede Protection:**  Incorporate a robust mechanism to prevent cache stampedes.
* **Configurable Serialization:**  Allow developers to configure serialization using a custom `IHybridCacheSerializer` interface and provide a default implementation using `ArrayPool` for pooled resource allocation.
* **Resiliency:** Integrate Polly for configurable resiliency policies to handle transient faults when accessing the distributed cache.
* **Cache-Database Synchronization:**  Provide a mechanism to abstract data source interactions and ensure consistency between the cache and the database.
* **OpenTelemetry Integration:** (Future)  Integrate with OpenTelemetry for observability and performance monitoring.
* **Adaptive Caching:** (Future)  Explore adaptive caching strategies to dynamically adjust cache behavior based on real-time conditions.

**3. Architecture**

The core of the hybrid cache will be the `MyHybridCache` class, implementing the `IMyHybridCache` interface. This class will manage interactions between the L1 and L2 caches, handle serialization, enforce stampede protection, and apply resiliency policies.

**3.1 Class Diagram**
+---------------------+      +------------------------+
|   IMyHybridCache   |      |  IHybridCacheSerializer  |
+---------------------+      +------------------------+
^                       ^
|                       |
+---------------------+      +------------------------+
|    MyHybridCache    |      |  DefaultSerializer      |
+---------------------+      +------------------------+
|
| uses
v
+---------------------+      +---------------------+
|       L1 Cache      |      |      L2 Cache       |
+---------------------+      +---------------------+
(FastMemCache/RCache)     (StackExchange.Redis)

**3.2 Components**

* **`IMyHybridCache`:**  Defines the core API for the hybrid cache, including methods like `GetOrCreateAsync`, `SetAsync`, `RemoveAsync`, etc.
* **`MyHybridCache`:** The concrete implementation of `IMyHybridCache`.
    * Manages the interaction between L1 and L2 caches.
    * Implements stampede protection using `ConcurrentDictionary` and `SemaphoreSlim`.
    * Handles serialization/deserialization using `IHybridCacheSerializer`.
    * Applies Polly policies for resiliency.
    * Provides an abstraction for data source interaction to facilitate cache-database synchronization.
* **`IHybridCacheSerializer`:** Defines an interface for custom serializers.
* **`DefaultSerializer`:**  A default implementation of `IHybridCacheSerializer` that uses `ArrayPool` for efficient memory management.
* **L1 Cache:** An in-memory cache implementation optimized for speed.
* **L2 Cache:**  `StackExchange.Redis` for distributed caching.

**4.  Detailed Design**

**4.1 Cache Operations**

* **`GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiration)`:**
    1. Check if the key exists in the L1 cache. If found, return the value.
    2. Acquire a semaphore lock for the key to prevent cache stampedes.
    3. Double-check the L1 cache in case another thread populated it while waiting for the lock.
    4. If not found in L1, check the L2 cache. If found, deserialize the value, store it in L1, and return it.
    5. If not found in either cache, execute the `factory` delegate to retrieve the value from the data source.
    6. Serialize the value and store it in both L1 and L2 caches with the specified expiration.
    7. Release the semaphore lock.

* **`SetAsync<T>(string key, T value, TimeSpan expiration)`:**
    1. Serialize the value using the configured serializer.
    2. Store the value in both L1 and L2 caches with the specified expiration.

* **`RemoveAsync(string key)`:**
    1. Remove the key from both L1 and L2 caches.

* **`RemoveByTagAsync(string tag)`:** (Future) 
    1. Implement tag-based invalidation for both L1 and L2 caches.

**4.2 Stampede Protection**

Use a `ConcurrentDictionary<string, SemaphoreSlim>` to store semaphores associated with each cache key. This ensures that only one thread can execute the `factory` delegate for a given key at a time.

**4.3 Serialization**

Use the `IHybridCacheSerializer` interface to allow developers to plug in custom serializers. Provide a default implementation (`DefaultSerializer`) that uses `ArrayPool` for efficient memory management.

**4.4 Resiliency**

Integrate Polly to handle transient faults when interacting with the L2 cache (StackExchange.Redis). Allow developers to configure retry policies, circuit breakers, and other resiliency mechanisms.

**4.5 Cache-Database Synchronization**

Introduce an interface (e.g., `IDataSource<T>`) to abstract interactions with the data source. The `factory` delegate in `GetOrCreateAsync` can accept an instance of this interface, enabling developers to implement custom logic for retrieving data and ensuring consistency between the cache and the database.

**5.  Implementation Details**

* **.NET Standard Target:** Target .NET Standard 2.0 for compatibility with a wide range of .NET versions.
* **Dependency Injection:** Use a builder pattern to facilitate easy configuration and registration of the hybrid cache with dependency injection in ASP.NET Core applications.
* **Configuration:** Provide options for configuring L1 and L2 cache settings, serialization, and Polly policies.

**6. Future Considerations**

* **OpenTelemetry Integration:** Add support for OpenTelemetry to capture cache metrics and tracing information.
* **Adaptive Caching:** Implement adaptive caching strategies to dynamically adjust cache behavior based on factors like cache hit rate and data volatility.
* **`IBufferWriter` and `IBufferDistributedCache`:** Explore the use of `IBufferWriter` and `IBufferDistributedCache` to optimize memory usage and reduce allocations, especially for large cache entries.
* **`IDistributedCache.Refresh()`:**  Investigate how to leverage the `Refresh()` method of `IDistributedCache` to extend the lifetime of cache entries in the distributed cache.