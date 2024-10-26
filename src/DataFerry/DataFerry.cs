using Microsoft.Extensions.Caching.Hybrid;

namespace lvlup.DataFerry
{
    public class DataFerry : HybridCache, IDataFerry
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
