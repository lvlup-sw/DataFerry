using CacheObject.Providers;
using MockCachingOperation.Process;

namespace MockCachingOperation
{
    public class RealProvider : IRealProvider<Payload>
    {
        private readonly IServiceProvider _serviceProvider;

        public RealProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

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

        public IServiceProvider ServiceProvider { get => _serviceProvider; }
    }
}