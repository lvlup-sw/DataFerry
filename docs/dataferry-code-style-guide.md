# DataFerry Code Style Guide

This style guide is based on the coding patterns observed in the ConcurrentPriorityQueue implementation and should be followed for consistency across the DataFerry library.

## File Structure

### 1. File Header
Every C# file must begin with the standard copyright header:
```csharp
// ===========================================================================
// <copyright file="FileName.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================
```

### 2. Using Statements
- System namespaces first, ordered alphabetically
- Third-party namespaces next
- Project namespaces last
- Separate groups with blank lines

```csharp
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;

using lvlup.DataFerry.Concurrency.Contracts;
using lvlup.DataFerry.Orchestrators.Contracts;
```

## Naming Conventions

### Fields and Constants
- **Private fields**: Prefix with underscore and use camelCase: `_maxSize`, `_count`
- **Public constants**: PascalCase with descriptive names: `DefaultMaxSize`, `DefaultPromotionProbability`
- **Private constants**: PascalCase: `InvalidLevel`, `BottomLevel`
- **Static fields**: Prefix with `s_`: `s_sequenceGenerator`

### Methods and Properties
- **Methods**: PascalCase: `TryAdd`, `ValidateInsertion`
- **Properties**: PascalCase: `Priority`, `IsDeleted`
- **Parameters**: camelCase: `priority`, `element`, `insertLevel`
- **Local variables**: camelCase: `curr`, `nodeToUpdate`

### Type Names
- **Classes/Interfaces**: PascalCase with descriptive names
- **Interfaces**: Prefix with 'I': `IConcurrentPriorityQueue`
- **Generic type parameters**: Use 'T' prefix: `TPriority`, `TElement`

## Code Organization

### 1. Regions
Use regions to organize code sections:
```csharp
#region Global Variables
#region Constructors
#region Core Operations
#region Shared Helper Methods
#region [Feature] Helper Methods
#region Internal Data Structures
#region Observability
```

### 2. Member Order Within Class
1. Constants (public then private)
2. Fields (readonly then mutable, static then instance)
3. Constructors
4. Properties
5. Public methods
6. Internal methods
7. Private methods
8. Nested types

## Documentation

### XML Documentation Requirements
All public and internal APIs must have comprehensive XML documentation:

```csharp
/// <summary>
/// Brief description of what the method does.
/// </summary>
/// <typeparam name="T">Description of generic type parameter.</typeparam>
/// <param name="paramName">Description of parameter purpose and constraints.</param>
/// <returns>Description of return value and possible states.</returns>
/// <exception cref="ExceptionType">When the exception is thrown.</exception>
/// <remarks>
/// <para>
/// Additional implementation details, complexity analysis, or usage notes.
/// </para>
/// <para>
/// Use multiple paragraphs for clarity.
/// </para>
/// </remarks>
```

### Documentation Style Guidelines
1. Start summary with a verb in third person: "Validates...", "Performs...", "Checks..."
2. Use `<c>` tags for inline code references: `<c>TryAdd</c>`
3. Use `<see cref=""/>` for type/member references
4. Include complexity analysis where relevant: "Expected O(log n) time complexity"
5. Document thread-safety guarantees
6. Use `<list type="bullet">` for enumerating features or conditions

## Method Design Patterns

### 1. Guard Clauses
Place parameter validation at method start:
```csharp
public bool TryAdd(TPriority priority, TElement element)
{
    ArgumentNullException.ThrowIfNull(priority, nameof(priority));
    ArgumentNullException.ThrowIfNull(element, nameof(element));
    // Method implementation
}
```

### 2. Try-Pattern Methods
- Return bool for success/failure
- Use `out` parameters for results
- Name with "Try" prefix: `TryAdd`, `TryDelete`, `TryDeleteMin`

### 3. Helper Method Patterns
- Mark as `internal static` when no instance state needed
- Group related helpers in regions
- Document preconditions and postconditions clearly

## Threading and Concurrency

### 1. Lock Management
```csharp
node.Lock();
try
{
    // Critical section
}
finally
{
    node.Unlock();
}
```

### 2. Memory Barriers
Document memory ordering requirements:
```csharp
// Ensure writes are visible before linking
Thread.MemoryBarrier();
```

### 3. Volatile Fields
Use for lock-free flags with clear documentation:
```csharp
private volatile bool _isInserted;  // Has release/acquire semantics
```

## Error Handling

### 1. Exception Usage
- Use built-in ArgumentException types for parameter validation
- Include parameter name: `nameof(parameter)`
- Provide clear exception messages

### 2. Validation Patterns
```csharp
ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1, nameof(maxSize));
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offsetK, nameof(offsetK));
```

## Performance Considerations

### 1. Lock-Free Operations
- Document when operations are lock-free
- Prefer lock-free reads where possible
- Use `Interlocked` operations for atomic updates

### 2. Memory Efficiency
- Use `ArrayPool<T>` for temporary buffers
- Document memory allocation patterns
- Consider array reuse in hot paths

## Code Comments

### 1. Implementation Comments
- Explain "why" not "what"
- Document complex algorithms or non-obvious logic
- Reference papers/algorithms: "Based on Lotan-Shavit algorithm"

### 2. TODO/FIXME Comments
Avoid these in production code. Use issues or documentation instead.

## Testing Considerations

### 1. Internal Visibility
Mark methods `internal` rather than `private` when testing needed:
```csharp
internal static bool NodeIsInvalidOrDeleted(SkipListNode? curr)
```

### 2. Testable Design
- Prefer dependency injection
- Use interfaces for mockable dependencies
- Avoid static dependencies

## Observability

### 1. Structured Logging
Use structured logging with clear parameter names:
```csharp
_logger.LogInformation("Node added for Priority: {Priority}. Count: {Count}", 
    priority, currentCount);
```

### 2. Metrics and Tracing
- Use semantic naming for metrics
- Include units in metric names
- Add appropriate tags for filtering

## Code Analysis

### Required Analyzers
The project uses these analyzers - ensure compliance:
- StyleCop.Analyzers
- Microsoft.CodeAnalysis.Analyzers
- Microsoft.VisualStudio.Threading.Analyzers

### Common Warnings to Address
- SA1633: File header missing
- SA1005/SA1512: Comment formatting
- CS8618: Non-nullable field warnings

## Example Method Following Style Guide

```csharp
/// <summary>
/// Validates that the insertion path is clear by locking predecessors and checking links.
/// </summary>
/// <param name="searchResult">The structural search result containing predecessors.</param>
/// <param name="insertLevel">The highest level index of the node being inserted.</param>
/// <param name="highestLevelLocked">Tracks the highest locked level.</param>
/// <returns><c>true</c> if the insertion path is valid; <c>false</c> otherwise.</returns>
/// <remarks>
/// Locks are acquired bottom-up to prevent deadlocks. The caller must unlock
/// predecessors using <paramref name="highestLevelLocked"/> in a finally block.
/// </remarks>
internal static bool ValidateInsertion(
    SearchResult searchResult, 
    int insertLevel, 
    ref int highestLevelLocked)
{
    bool isValid = true;

    // Iterate from bottom up to insertion level
    for (int level = BottomLevel; isValid && level <= insertLevel; level++)
    {
        var predecessor = searchResult.GetPredecessor(level);
        var successor = searchResult.GetSuccessor(level);

        // Lock predecessor before validation
        predecessor.Lock();
        
        // Track highest level for cleanup
        Interlocked.Exchange(ref highestLevelLocked, level);

        // Validate predecessor state and linkage
        isValid = !predecessor.IsDeleted &&
                  !successor.IsDeleted &&
                  predecessor.GetNextNode(level) == successor;
    }

    return isValid;
}
```