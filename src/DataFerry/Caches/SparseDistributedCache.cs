using Microsoft.Extensions.Caching.Distributed;
using Polly.Wrap;
using StackExchange.Redis;
using System.Buffers;

namespace lvlup.DataFerry.Caches
{
    public class SparseDistributedCache : ISparseDistributedCache
    {
        public ValueTask GetBatchFromCacheAsync<T>(IEnumerable<string> keys, Action<string, T?> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public IDatabase GetCacheConnection()
        {
            throw new NotImplementedException();
        }

        public bool GetFromCache(string key, IBufferWriter<byte> destination)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> GetFromCacheAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public AsyncPolicyWrap<object> GetPollyPolicy()
        {
            throw new NotImplementedException();
        }

        public ValueTask RefreshBatchFromCacheAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public bool RefreshInCache(string key)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RefreshInCacheAsync(string key, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask RemoveBatchFromCacheAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public bool RemoveFromCache(string key)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RemoveFromCacheAsync(string key, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask SetBatchInCacheAsync(IDictionary<string, ReadOnlySequence<byte>> data, DistributedCacheEntryOptions options, TimeSpan? absoluteExpiration, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public void SetFallbackValue(object value)
        {
            throw new NotImplementedException();
        }

        public bool SetInCache(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> SetInCacheAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }
}
