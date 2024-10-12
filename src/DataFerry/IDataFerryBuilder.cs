using Microsoft.Extensions.DependencyInjection;

namespace lvlup.DataFerry
{
    /// <summary>
    /// Helper API for configuring <see cref="Providers.DataFerry"/>.
    /// </summary>
    public interface IDataFerryBuilder
    {
        /// <summary>
        /// Gets the services collection associated with this instance.
        /// </summary>
        IServiceCollection Services { get; }
    }
}
