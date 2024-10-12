using Microsoft.Extensions.DependencyInjection;

namespace lvlup.DataFerry
{
    /// <summary>
    /// Helper API for configuring <see cref="Providers.DataFerry"/>.
    /// </summary>
    public sealed class DataFerryBuilder(IServiceCollection services) : IDataFerryBuilder
    {

        /// <summary>
        /// Gets the services collection associated with this instance.
        /// </summary>
        public IServiceCollection Services { get; } = services;
    }
}
