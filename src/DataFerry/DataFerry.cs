using Microsoft.Extensions.Caching.Hybrid;
using System.Diagnostics.CodeAnalysis;
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

    internal readonly struct StampedeKey : IEquatable<StampedeKey>
    {
        private readonly string _key;
        private readonly HybridCacheEntryFlags _flags;
        private readonly int _hashCode;

        public StampedeKey(string key, HybridCacheEntryFlags flags)
        {
            // We'll use both the key *and* the flags as combined flag; in reality, we *expect*
            // the flags to be consistent between calls on the same operation, and it must be
            // noted that the *cache items* only use the key (not the flags), but: it gets
            // very hard to grok what the correct behaviour should be if combining two calls
            // with different flags, since they could have mutually exclusive behaviours!

            // As such, we'll treat conflicting calls entirely separately from a stampede
            // perspective.
            _key = key;
            _flags = flags;
            _hashCode = System.HashCode.Combine(key, flags);
        }

        public string Key => _key;

        public HybridCacheEntryFlags Flags => _flags;

        // Allow direct access to the pre-computed hash-code, semantically emphasizing that
        // this is a constant-time operation against a known value.
        internal int HashCode => _hashCode;

        public bool Equals(StampedeKey other) => _flags == other._flags & _key == other._key;

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is StampedeKey other && Equals(other);

        public override int GetHashCode() => _hashCode;

        public override string ToString() => $"{_key} ({_flags})";
    }

    internal abstract class StampedeState : IThreadPoolWorkItem
    {
        internal readonly CancellationToken SharedToken; // this might have a value even when _sharedCancellation is null

        // Because multiple callers can enlist, we need to track when the *last* caller cancels
        // (and keep going until then); that means we need to run with custom cancellation.
        private readonly CancellationTokenSource? _sharedCancellation;
        private readonly DataFerry _cache;
        private readonly CacheItem _cacheItem;

        // we expose the key as a by-ref readonly; this minimizes the stack work involved in passing the key around
        // (both in terms of width and copy-semantics)
        private readonly StampedeKey _key;
        public ref readonly StampedeKey Key => ref _key;
        protected CacheItem CacheItem => _cacheItem;

        /// <summary>
        /// Initializes a new instance of the <see cref="StampedeState"/> class optionally with shared cancellation support.
        /// </summary>
        protected StampedeState(DataFerry cache, in StampedeKey key, CacheItem cacheItem, bool canBeCanceled)
        {
            _cache = cache;
            _key = key;
            _cacheItem = cacheItem;
            if (canBeCanceled)
            {
                // If the first (or any) caller can't be cancelled;,we'll never get to zero: n point tracking.
                // (in reality, all callers usually use the same path, so cancellation is usually "all" or "none")
                _sharedCancellation = new();
                SharedToken = _sharedCancellation.Token;
            }
            else
            {
                SharedToken = CancellationToken.None;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StampedeState"/> class using a fixed cancellation token.
        /// </summary>
        protected StampedeState(DataFerry cache, in StampedeKey key, CacheItem cacheItem, CancellationToken token)
        {
            _cache = cache;
            _key = key;
            _cacheItem = cacheItem;
            SharedToken = token;
        }

        protected DataFerry Cache => _cache;

        public abstract void Execute();

        public override string ToString() => Key.ToString();

        public abstract void SetCanceled();

        public int DebugCallerCount => _cacheItem.RefCount;

        public abstract Type Type { get; }

        public void CancelCaller()
        {
            // note that TryAddCaller has protections to avoid getting back from zero
            if (_cacheItem.Release())
            {
                // we're the last to leave; turn off the lights
                _sharedCancellation?.Cancel();
                SetCanceled();
            }
        }

        public bool TryAddCaller() => _cacheItem.TryReserve();

        /*
        private void RemoveStampedeState(in StampedeKey key)
        {
            // see notes in SyncLock.cs
            lock (GetPartitionedSyncLock(in key))
            {
                _ = _currentOperations.TryRemove(key, out _);
            }
        }
        */
    }
}