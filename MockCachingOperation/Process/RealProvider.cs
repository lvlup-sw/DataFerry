using CacheProvider.Providers.Interfaces;

namespace MockCachingOperation.Process
{
    /// <summary>
    /// This is an example RealProvider class that implements the <see cref="IRealProvider{T}"/> interface.
    /// </summary>
    public class RealProvider() : IRealProvider<Payload>
    {
        /// <summary>
        /// Get object from data source
        /// </summary>
        /// <remarks>
        /// This method simply reverses the data and sets the property to true to imitate a real data source.
        /// </remarks>
        /// <param name="payload"></param>
        /// <returns><see cref="Task"/> of type <see cref="Payload"/></returns>
        public async Task<Payload> GetAsync(Payload payload)
        {
            List<string> newData = payload.Data?
                .Select(data => new string(data.Reverse().ToArray()))
                .ToList() ?? [];

            // Simulate a delay
            await Task.Delay(30);
            payload.Data = newData;
            return payload;
        }

        /// <summary>
        /// Get object from data source
        /// </summary>
        public Payload Get(Payload payload)
        {
            List<string> newData = payload.Data?
                .Select(data => new string(data.Reverse().ToArray()))
                .ToList() ?? [];

            payload.Data = newData;
            return payload;
        }
    }
}