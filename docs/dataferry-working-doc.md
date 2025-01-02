# DataFerry Working Doc

## DistributedCache Data Flow
- When the `DistributedCache` needs to retrieve a value from Redis, it uses the `RentedBufferWriter` to obtain a buffer.
- The `RentedBufferWriter` internally uses the `StackArrayPool<byte>` to rent a buffer, avoiding a new allocation if possible.
- The `DistributedCache` retrieves the raw bytes from Redis and copies them into the rented buffer provided by the `RentedBufferWriter`.
- The `DistributedCache` then passes the `RentedBufferWriter` (which encapsulates the rented buffer) to the `DistributedCacheSerializer`.
- The `DistributedCacheSerializer` uses the buffer within the `RentedBufferWriter` to perform the deserialization, potentially renting another buffer from the `StackArrayPool<byte>` if needed for temporary storage during deserialization.
- The deserialized object is returned.
- The `RentedBufferWriter` is disposed of, which returns the rented buffer to the `StackArrayPool<byte>`.

## LfuMemCache Design Overview
The LfuMemCache is an approximation of an LFU cache. The main cache follows a typical segmented LRU (SLRU) pattern, which includes probation and protected segments.

In an SLRU cache, items are initially admitted to the probation segment. Frequently used items are then promoted to the protected segment, which acts as a "hot-path" for the most utilized cache items.  Items in the protected segment are less likely to be evicted.

Admission to the main cache follows the window TinyLFU (W-TinyLFU) policy. This policy uses a small window (1% of the total cache size) and a frequency histogram to filter entrants. By filtering items through this window, the cache ensures that only frequently accessed items are admitted.

The key innovation behind W-TinyLFU is its use as an admission policy, not an eviction policy. This means that the cache doesn't need to maintain a strict LFU order for all items in the cache. Instead, the frequency histogram only needs to determine if an eviction candidate (already in the main cache) is more popular than the entrant candidate; it need not determine the exact ordering between all items in the cache.

The LfuMemCache draws significant inspiration from the Caffeine and BitFaster libraries, in some cases directly adapting and incorporating elements from their designs.

## Architectural Diagram
```
graph LR
    subgraph " "
        direction LR
        RW["Read<br>Buffer"] --> Cache["Cache"]
        WB["Write<br>Buffer"] --> Cache
    end

    subgraph "Cache"
        direction LR
        Window["Window"] --> Probation["Probation"]
        Window --> Protected["Protected"]
    end

    CMS["Count-Min Sketch"] --> Window

    PQ["Priority Queue"] --> Window

    Cache --> TW["Background Task<br>Orchestrator"]

    TW --> Threads["Dedicated<br>Threads"]

    classDef default fill:#f9f,stroke:#333,stroke-width:2px;
    classDef buffer fill:#fff,stroke:#d93,stroke-width:2px;
    class RW,WB buffer;
    classDef window fill:#ccf,stroke:#333,stroke-width:2px;
    class Window window;
    classDef segment fill:#ccf,stroke:#333,stroke-width:2px;
    class Probation,Protected segment;
    classDef queue fill:#d9d,stroke:#909,stroke-width:2px;
    class PQ queue;
    classDef timerWheel fill:#dfd,stroke:#090,stroke-width:2px;
    class TW timerWheel;
    classDef cms fill:#ff9,stroke:#d93,stroke-width:2px;
    class CMS cms;
```

## ConcurrentPriorityQueue:

### 2.0.0 Alpha Release

-   [x] Clear()

### 2.0.0 Full Release

-   [ ] Duplicate Keys (buggy)
-   [ ] Unit Tests

## LfuMemCache:

### 2.0.0 Alpha Release

-   [x] SLRU logic
-   [x] Promotion/Demotion
-   [x] Eviction

### 2.0.0 Full Release

-   [ ] Unit Tests

## DataFerry:

### 2.0.0 Alpha Release

-   [x] Basic Implementation
-   [x] Resolve Inheritances

### 2.0.0 Full Release

-   [ ] Cache Stampede Protection
-   [ ] OpenTelemetry
-   [ ] Options Classes
-   [ ] Unit Tests

### 2.1.0 Release

-   [ ] NetStandard Compatability
-   [ ] Serializer Customization

### 2.2.0 Release

-   [ ] Tagging
-   [ ] Perf Drilldown