using CacheProvider.Providers;

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
        public async Task<Payload> GetItemAsync(Payload payload)
        {
            List<string> newData = [];
            foreach (var data in payload.Data!)
            {
                newData.Add(new string(data.Reverse().ToArray()));
            }
            payload.Data = newData;
            payload.Property = true;
            await Task.Delay(50);
            return await Task.FromResult(payload);
        }
    }
}