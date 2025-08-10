# ConcurrentPriorityQueue: A Deep Dive

## 1. Executive Summary

The `ConcurrentPriorityQueue<TPriority, TElement>` is a high-performance, thread-safe priority queue engineered for demanding concurrent applications. It is implemented as a **lock-based SkipList with relaxed semantics**, based on the **Lotan-Shavit algorithm**.

Its design prioritizes minimizing contention and maximizing throughput by employing several advanced concurrency techniques:
-   **Fine-Grained Locking:** Locks are placed on individual nodes, not the entire collection, allowing multiple threads to operate on different parts of the list simultaneously.
-   **Logical Deletion:** Nodes are marked as "deleted" in a fast, atomic step. The more expensive work of physically unlinking the node is offloaded to a background task orchestrator.
-   **Contention-Reducing Algorithms:** It uses the **SprayList** algorithm for near-minimum deletion (`TryDeleteMin`), which distributes write pressure away from the head of the list, a common bottleneck.
-   **Efficient Resource Management:** It leverages `ObjectPool` and `ArrayPool` to significantly reduce garbage collection pressure under high load.

This document provides a meticulous breakdown of its internal architecture, algorithms, and concurrency control mechanisms.

---

## 2. Core Architecture & Data Structures

The queue is built upon three fundamental components: the `SkipListNode`, the `SearchResult` that describes list positions, and the `ITaskOrchestrator` for background work.

### 2.1. `SkipListNode<TPriority, TElement>`

This is the atomic unit of the SkipList. Each node contains the user's data and the necessary metadata for concurrency and list structure.

-   **Identity and Ordering:**
    -   `Priority`: The user-provided priority value.
    -   `SequenceNumber`: A statically incremented `long`. This is a crucial tie-breaker. When two nodes have the same `Priority`, the one with the lower `SequenceNumber` is considered to have higher priority (it was created earlier). This ensures a stable, unique ordering for every node.
    -   `Type`: An enum (`Head`, `Tail`, `Data`) identifying the sentinel nodes from regular data nodes.

-   **Concurrency and State Management:**
    -   `nodeLock`: A private `System.Threading.Lock` instance providing fine-grained, node-level locking.
    -   `volatile bool IsInserted`: A flag indicating if the node has been fully linked into the list. Its volatility ensures that changes are immediately visible to all threads. The point at which this is set to `true` is the **linearization point** for an insertion.
    -   `volatile bool IsDeleted`: A flag indicating if the node has been logically removed. Its volatility is critical for lock-free reads. The point at which this is set to `true` is the **linearization point** for a deletion.

-   **Structure:**
    -   `nextNodeArray`: An array of references to the next node at each level. The length of this array defines the node's height (`TopLevel`).

-   **Pooling and Re-initialization:**
    -   Nodes are designed to be pooled. The `Reinitialize` method resets a recycled node's state (`Priority`, `Element`, `SequenceNumber`, flags) and resizes its `nextNodeArray` as needed, avoiding the cost of new object allocation.

### 2.2. `SearchResult<TPriority, TElement>`

This `IDisposable` class is a transient object that encapsulates the result of a `StructuralSearch`. It is a critical component for ensuring atomicity during mutations.

-   **Purpose:** It holds the arrays of `predecessor` and `successor` nodes that bracket a target position at every level of the SkipList.
-   **Resource Management:** To avoid allocations during high-frequency search operations, the `predecessorArray` and `successorArray` are rented from the shared `ArrayPool<SkipListNode<TPriority, TElement>>`. The `Dispose` method is responsible for returning these arrays to the pool.
-   **State:**
    -   `IsFound`: Indicates whether the search located an exact node match.
    -   `NodeFound`: A reference to the exact node if `IsFound` is true.

### 2.3. `ITaskOrchestrator`

This interface represents a background task scheduler.
-   **Role:** It decouples the fast, logical deletion of a node from the slower, more complex physical deletion.
-   **Process:** When a node is successfully marked for deletion, `SchedulePhysicalNodeRemoval` submits a `Func<Task>` to the orchestrator. A background worker thread from the orchestrator eventually executes this task to perform the actual unlinking.
-   **Resilience:** This design prevents application threads from being blocked by cleanup work. If the orchestrator's queue is full, it may drop the task (logged as a warning), which is an acceptable tradeoff, as the logically deleted node is already invisible and will be skipped by all future operations.

---

## 3. Algorithms and Execution Flow

### 3.1. Insertion: `TryAdd`

The insertion process follows the Lotan-Shavit algorithm, a carefully orchestrated sequence of lock-free searching and lock-based validation and modification.

1.  **Initialization:**
    -   A random height for the new node is determined by `GenerateLevel()`, based on the configured `PromotionProbability`.
    -   A `SkipListNode` is retrieved from the `_nodePool` and re-initialized with the user's data and the new height.

2.  **Attempt Loop:** The operation enters a `while(true)` loop to handle retries in case of concurrent modifications by other threads.

3.  **Step 1: Structural Search (Lock-Free)**
    -   `StructuralSearch(newNode)` is called. It traverses the SkipList from the top level down, without acquiring any locks, to find the exact position where the `newNode` should be inserted based on its `(Priority, SequenceNumber)` tuple.
    -   It returns a `SearchResult` object containing the arrays of predecessors and successors that will surround the `newNode` at every level.

4.  **Step 2: Validation (Locking)**
    -   `ValidateInsertion()` is called. This is the critical step for ensuring atomicity.
    -   It iterates from the bottom level (`0`) up to the `insertLevel` of the new node.
    -   At each level `L`, it **acquires the lock on the predecessor node** (`predecessor.Lock()`).
    -   It then validates two conditions:
        1.  The predecessor has not been concurrently deleted (`!predecessor.IsDeleted`).
        2.  The predecessor's `next` pointer at level `L` still points to the successor found during the lock-free search (`predecessor.GetNextNode(level) == successor`).
    -   If any validation check fails, the process is aborted (locks acquired so far are released by the `finally` block in `TryAdd`), and the `while(true)` loop retries the entire operation. The bottom-up locking order is crucial for preventing deadlocks.

5.  **Step 3: Physical Insertion (Under Lock)**
    -   If validation succeeds, the predecessors at all relevant levels are now locked.
    -   `InsertNode()` performs the physical linking. It first sets all of the `newNode`'s `next` pointers to point to its successors. A `Thread.MemoryBarrier()` ensures this internal state is visible before proceeding.
    -   It then updates the `next` pointers of the (locked) predecessors to point to the `newNode`.

6.  **Step 4: Linearization**
    -   The line `newNode.IsInserted = true;` is the **linearization point**. Once this volatile flag is set, the new node is officially part of the queue and is visible to all other threads.

7.  **Step 5: Cleanup and Bounding**
    -   The locks on the predecessors are released.
    -   The count is incremented. If the new count exceeds `_maxSize`, `TryDeleteMin()` is called to evict a near-minimum node, thus enforcing the queue's bound.

### 3.2. Deletion: `TryRemove` and `TryDeleteAbsoluteMin`

Deletion follows a similar pattern but introduces the concept of logical removal.

1.  **Step 1: Candidate Search (Lock-Free)**
    -   `TryRemove`: Uses `InlineSearch()` to perform a fast, lock-free scan on the bottom level (`Level 0`) to find the first valid (not deleted) node matching the specified priority.
    -   `TryDeleteAbsoluteMin`: Simply starts at `_head.GetNextNode(BottomLevel)` to find the first valid node.

2.  **Step 2: Logical Deletion (Locking)**
    -   `LogicallyDeleteNode(nodeToDelete)` is called.
    -   It attempts to acquire the lock on the target node using `TryEnter()`. If it fails, it returns `false`, signaling contention and causing the caller to retry or fail.
    -   If the lock is acquired, it checks if the node has already been deleted.
    -   `nodeToDelete.IsDeleted = true;` is the **linearization point**. The node is now logically removed.
    -   **Crucially, if successful, this method returns `true` *without releasing the lock*.** The caller is now responsible for the lock.

3.  **Step 3: Post-Deletion Validation (Locking)**
    -   (`TryRemove` only) `TryValidateAfterLogicalDelete()` is called to ensure the list structure around the now-deleted node is still consistent. This is a safeguard against certain race conditions and involves locking predecessors to validate their pointers, similar to insertion.

4.  **Step 4: Schedule Physical Removal**
    -   `SchedulePhysicalNodeRemoval()` is called. It submits a task to the `ITaskOrchestrator`.
    -   The background task will later perform a top-down traversal to find the predecessors of the logically deleted node, lock them, and update their `next` pointers to bypass the node, physically unlinking it.
    -   Unlinking top-down preserves the SkipList invariant ("list at level L is a subset of list at L-1").
    -   After unlinking, the node object is returned to the `_nodePool`.

5.  **Step 5: Cleanup**
    -   The lock on the logically deleted node is finally released.
    -   The count is decremented.

### 3.3. Probabilistic Deletion: `TryDeleteMin` and the SprayList Algorithm

This is the queue's most advanced feature, designed to mitigate head-of-line contention when the queue is at or near capacity.

-   **The Problem:** In a full queue, every `TryAdd` triggers a `TryDeleteMin`. If `TryDeleteMin` always targets the absolute first node, that node becomes a massive point of contention.
-   **The Solution:** The SprayList algorithm finds a node that is *near* the minimum but is chosen probabilistically. This spreads the delete operations across a small group of nodes at the front of the list.

-   **Execution Flow:**
    1.  **Parameter Calculation:** `SprayParameters.CalculateParameters` determines the parameters for a random walk based on the current queue size (`currentCount`) and tuning constants (`OffsetK`, `OffsetM`).
        -   `StartHeight (h)`: `log(n) + K`. The walk starts high in the list.
        -   `MaxJumpLength (y)`: `M * (log(n))^3`. The maximum horizontal distance to travel at each level.
        -   `DescentLength (d)`: `log(log(n))`. The number of levels to drop after each horizontal spray.
    2.  **The "Spray":** `SpraySearch()` performs the random walk.
        -   It starts at the `head` node at `StartHeight`.
        -   It generates a random jump length up to `MaxJumpLength` and traverses forward at the current height.
        -   It then descends `DescentLength` levels.
        -   This process repeats until the walk reaches the bottom of the list. The node it lands on is the candidate for deletion.
    3.  **Deletion Attempt:** The algorithm then attempts to delete the candidate node using the standard `LogicallyDeleteNode` and `SchedulePhysicalNodeRemoval` process.
    4.  **Probabilistic Retry:** If the logical deletion fails (e.g., another thread deleted the node first), it may retry the entire `TryDeleteMin` operation with a reduced probability, preventing livelocks.

---

## 4. Concurrency Control Deep Dive

-   **Locking Strategy:** Fine-grained locking on individual `SkipListNode` objects. The critical invariant is that **locks on predecessors are acquired in a bottom-up order** during mutation to prevent deadlocks. The order of lock release is not critical.

-   **Memory Model and Visibility:**
    -   The `volatile` keyword on `IsInserted` and `IsDeleted` is essential. It ensures that writes to these flags have *release semantics* and reads have *acquire semantics*. This guarantees that once a flag is written, its new value is immediately visible to all other threads, and any memory writes that happened *before* the flag was set are also visible.
    -   `Thread.MemoryBarrier()` is used strategically in `InsertNode`. It ensures that the new node's internal `_nextNodeArray` is fully initialized and visible in memory *before* any external predecessor nodes are modified to point to it. This prevents other threads from observing a partially constructed node.

-   **Linearization Points:** These are the single, atomic instructions that define the exact moment an operation takes effect.
    -   **`TryAdd`:** `newNode.IsInserted = true;`
    -   **`TryRemove` / `TryDeleteMin`:** `curr.IsDeleted = true;`

-   **Invariants:**
    -   The primary structural invariant is that **the list at any level `L` is a subset of the list at level `L-1`**.
    -   This is maintained by:
        1.  **Inserting bottom-up:** A node is linked into level `L` only after it has been linked into level `L-1`.
        2.  **Physically removing top-down:** A node is unlinked from level `L` before it is unlinked from level `L-1`.

---

## 5. Configuration, Tuning, and Observability

### 5.1. `ConcurrentPriorityQueueOptions`

-   **`MaxSize`**: Controls the queue's capacity. Setting to `int.MaxValue` creates an effectively unbounded queue.
-   **`PromotionProbability`**: Controls the height of the SkipList. The default of `0.5` is standard. Higher values lead to taller, sparser lists (faster search, more memory), while lower values lead to shorter, denser lists (slower search, less memory).
-   **`SprayOffsetK` & `SprayOffsetM`**: These directly tune the SprayList algorithm. `K` affects the starting height, and `M` scales the horizontal jump distance. Increasing `M` makes the spray "wider," distributing contention more but increasing the chance of deleting a node with a slightly higher priority. These should generally not be modified without specific performance testing.

### 5.2. Observability

The `ConcurrentPriorityQueueObservabilityHelper` provides deep insight into the queue's behavior.
-   **Metrics (`System.Diagnostics.Metrics`):**
    -   **Counters:** `priorityqueue.adds.count`, `priorityqueue.deletes.count`, `priorityqueue.updates.count`, `priorityqueue.operations.failed.count`. Deletes and failures are tagged by type (`min`, `absolute_min`, `specific`).
    -   **Histograms:** `priorityqueue.add.duration`, `priorityqueue.delete.duration`. These are critical for understanding performance under load.
    -   **Gauges:** `priorityqueue.fullness.ratio` (current count / max size) and `priorityqueue.current.count`.
-   **Tracing (`System.Diagnostics.ActivitySource`):**
    -   All public methods create an `Activity`, enabling them to be captured by distributed tracing systems like OpenTelemetry. Activities are tagged with the operation name and priority.
-   **Logging (`Microsoft.Extensions.Logging`):**
    -   Provides detailed logs for initialization, operation outcomes, and significant events like exceeding max size or dropping physical removal tasks.

This robust observability is not an afterthought; it is a core feature essential for diagnosing performance issues in a complex concurrent system.