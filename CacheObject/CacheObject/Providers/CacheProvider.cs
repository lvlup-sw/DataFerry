﻿using CacheObject.Caches;
using Microsoft.Extensions.DependencyInjection;

namespace CacheObject.Providers
{
    public class CacheProvider<T> : ICacheProvider<T>, IDisposable where T : class
    {
        private bool disposedValue;
        private readonly IServiceProvider? _serviceProvider;
        private readonly IRealProvider<T> _realProvider;
        private readonly ICache<T> _cache;

        public CacheProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _realProvider = _serviceProvider.GetService<IRealProvider<T>>() ?? throw new InvalidOperationException($"Could not retrieve service of type {typeof(IRealProvider<T>)}");
            _cache = _serviceProvider.GetService<ICache<T>>() ?? throw new InvalidOperationException($"Could not retrieve service of type {typeof(ICache<T>)}");
        }

        public CacheProvider(ICache<T> cache, IRealProvider<T> realProvider)
        {
            _realProvider = realProvider;
            _cache = cache;
        }

        public IServiceProvider ServiceProvider { get => _serviceProvider!; }

        public IRealProvider<T> RealProvider { get => _realProvider; }

        public ICache<T> Cache { get => _cache; }

        public async Task<T> CacheObjectAsync(T obj, string key)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(obj);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            // Check if the item is in the cache
            var cachedItem = await _cache.GetItemAsync(key);
            if (cachedItem != null) 
            {
                return cachedItem;
            }

            // If not, get the item from the real provider and set it in the cache
            cachedItem = await _realProvider.GetObjectAsync(obj);
            await _cache.SetItemAsync(key, cachedItem);
            return cachedItem;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
