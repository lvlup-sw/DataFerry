using Microsoft.Extensions.Caching.Hybrid;
using System.Runtime.CompilerServices;

namespace lvlup.DataFerry
{
    // We also need to implement Stampede Protection
    public class DataFerry : HybridCache, IDataFerry
    {
        public T GetOrCreate<TState, T>(
            string key, 
            TState state, 
            Func<TState, CancellationToken, T> factory, 
            HybridCacheEntryOptions? options = null, 
            IEnumerable<string>? tags = null)
        {
            throw new NotImplementedException();
        }

        public void Set<T>(
            string key, 
            T value, 
            HybridCacheEntryOptions? options = null, 
            IEnumerable<string>? tags = null)
        {
            throw new NotImplementedException();
        }

        public void Refresh(string key, HybridCacheEntryOptions options)
        {
            throw new NotImplementedException();
        }

        public void RefreshByTag(string tag, HybridCacheEntryOptions options)
        {
            throw new NotImplementedException();
        }

        public void Remove(string key)
        {
            throw new NotImplementedException();
        }

        public void RemoveByTag(string tag)
        {
            throw new NotImplementedException();
        }

        public override async ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, 
            TState state, Func<TState, CancellationToken, ValueTask<T>> factory, 
            HybridCacheEntryOptions? options = null, 
            IEnumerable<string>? tags = null, 
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override async ValueTask SetAsync<T>(
            string key, 
            T value, 
            HybridCacheEntryOptions? options = null, 
            IEnumerable<string>? tags = null, 
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async ValueTask RefreshAsync(string key, HybridCacheEntryOptions options, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async ValueTask RefreshByTagAsync(string tag, HybridCacheEntryOptions options, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override async ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<KeyValuePair<string, T>> GetOrCreateBatchAsync<TState, T>(
            IEnumerable<string> keys, 
            TState state, 
            Func<TState, string, CancellationToken, ValueTask<T>> factory, 
            HybridCacheEntryOptions? options = null, 
            IEnumerable<string>? tags = null, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<ValueTask> SetBatchAsync<T>(
            IEnumerable<string> keys,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<ValueTask> RefreshBatchAsync(
            IEnumerable<string> keys, 
            HybridCacheEntryOptions options, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<ValueTask> RefreshByTagBatchAsync(
            IEnumerable<string> tags, 
            HybridCacheEntryOptions options, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<ValueTask> RemoveBatchAsync(IEnumerable<string> keys, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<ValueTask> RemoveByTagBatchAsync(IEnumerable<string> keys, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
            throw new NotImplementedException();
        }
    }
}
