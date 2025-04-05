// =================================================================
// <copyright file="CacheSettings.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// =================================================================

namespace lvlup.DataFerry.Properties;

/// <summary>
/// Cache settings for <see cref="DataFerry"/>.
/// </summary>
public class CacheSettings
{
    /// <summary>
    /// Retrieves or sets the expiration of the cache in hours.
    /// </summary>
    public int AbsoluteExpiration { get; init; } = 24;

    /// <summary>
    /// Retrieves or sets the expiration of the memory cache in minutes.
    /// </summary>
    public int InMemoryAbsoluteExpiration { get; init; } = 60;

    /// <summary>
    /// Set to true to use In-Memory Caching.
    /// </summary>
    public bool UseMemoryCache { get; init; } = true;
}