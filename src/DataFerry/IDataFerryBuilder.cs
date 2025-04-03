// =====================================================================
// <copyright file="IDataFerryBuilder.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// =====================================================================

using Microsoft.Extensions.DependencyInjection;

namespace lvlup.DataFerry;

/// <summary>
/// Helper API for configuring <see cref="DataFerry"/>.
/// </summary>
public interface IDataFerryBuilder
{
    /// <summary>
    /// Gets the services collection associated with this instance.
    /// </summary>
    IServiceCollection Services { get; }
}