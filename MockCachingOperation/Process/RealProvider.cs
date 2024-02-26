using CacheObject.Providers;

namespace MockCachingOperation.Process
{
    /// <summary>
    /// This is an example RealProvider class that implements the <see cref="IRealProvider{T}"/> interface.
    /// </summary>
    /// <param name="serviceProvider"></param>
    public class RealProvider(IServiceProvider serviceProvider) : IRealProvider<Payload>
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        /// <summary>
        /// Get object from data source
        /// </summary>
        /// <remarks>
        /// This method simply reverses the data and sets the property to true to imitate a real data source.
        /// </remarks>
        /// <param name="payload"></param>
        /// <returns><see cref="Task"/> of type <see cref="Payload"/></returns>
        public Task<Payload> GetObjectAsync(Payload payload)
        {
            List<string> newData = [];
            foreach (var data in payload.Data!)
            {
                newData.Add(new string(data.Reverse().ToArray()));
            }
            payload.Data = newData;
            payload.Property = true;
            return Task.FromResult(payload);
        }

        /// <summary>
        /// Returns the service provider used to initialize the class.
        /// </summary>
        public IServiceProvider ServiceProvider { get => _serviceProvider; }
    }
}