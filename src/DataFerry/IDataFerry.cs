using Microsoft.Extensions.Caching.Hybrid;
using System.Runtime.CompilerServices;

namespace lvlup.DataFerry
{
    public interface IDataFerry
    {
        T GetOrCreate<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, T> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null);

        void Set<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null);

        void Refresh(string key, HybridCacheEntryOptions options);

        void RefreshByTag(string tag, HybridCacheEntryOptions options);

        void Remove(string key);

        void RemoveByTag(string tag);

        ValueTask RefreshAsync(string key, HybridCacheEntryOptions options, CancellationToken cancellationToken = default);

        ValueTask RefreshByTagAsync(string tag, HybridCacheEntryOptions options, CancellationToken cancellationToken = default);

        IAsyncEnumerable<KeyValuePair<string, T>> GetOrCreateBatchAsync<TState, T>(
            IEnumerable<string> keys,
            TState state,
            Func<TState, string, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default);

        IAsyncEnumerable<ValueTask> SetBatchAsync<T>(
            IEnumerable<string> keys,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default);

        IAsyncEnumerable<ValueTask> RefreshBatchAsync(IEnumerable<string> keys, HybridCacheEntryOptions options, CancellationToken cancellationToken = default);

        IAsyncEnumerable<ValueTask> RefreshByTagBatchAsync(IEnumerable<string> tags, HybridCacheEntryOptions options, CancellationToken cancellationToken = default);

        IAsyncEnumerable<ValueTask> RemoveBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

        IAsyncEnumerable<ValueTask> RemoveByTagBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
    }
}