// ===========================================================================
// <copyright file="SkipListNodePooledObjectPolicy.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using Microsoft.Extensions.ObjectPool;

namespace lvlup.DataFerry.Concurrency;

/// <summary>
/// Pooled object policy for <see cref="SkipListNode{TPriority, TElement}"/> instances.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the node.</typeparam>
internal sealed class SkipListNodePooledObjectPolicy<TPriority, TElement> : IPooledObjectPolicy<SkipListNode<TPriority, TElement>>
{
    /// <inheritdoc/>
    public SkipListNode<TPriority, TElement> Create()
    {
        // Create a default data node that can be reinitialized later
        // We'll create it with a minimal height (0) and reinitialize when needed
        return new SkipListNode<TPriority, TElement>(default!, default!, 0, null);
    }

    /// <inheritdoc/>
    public bool Return(SkipListNode<TPriority, TElement> obj)
    {
        // Only pool data nodes, not head/tail nodes
        if (obj?.Type is not SkipListNode<TPriority, TElement>.NodeType.Data) return false;

        // Reset the node state for reuse
        obj.IsDeleted = false;
        obj.IsInserted = false;
        obj.Element = default!;

        // Clear all next pointers
        for (int level = 0; level <= obj.TopLevel; level++)
        {
            obj.SetNextNode(level, null!);
        }

        return true;
    }
}