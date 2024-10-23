using lvlup.DataFerry.Caches;

namespace lvlup.DataFerry.Tests
{
    [TestClass]
    public class LfuMemCacheTests
    {
        private LfuMemCache<string, int> _cache = default!;

        [TestInitialize]
        public void Setup()
        {
            _cache = new(100, 200);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cache.Dispose();
        }

        [TestMethod]
        public async Task TestGetSetCleanup()
        {
            _cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(_cache.TryGet("42", out int v));
            Assert.IsTrue(v == 42);

            await Task.Delay(300);
            Assert.IsTrue(_cache.Count == 0);
        }

        [TestMethod]
        public async Task TestEviction()
        {
            var list = new List<LfuMemCache<int, int>>();
            for (int i = 0; i < 20; i++)
            {
                var cache = new LfuMemCache<int, int>(cleanupJobInterval: 200);
                cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(100));
                list.Add(cache);
            }
            await Task.Delay(300);

            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(list[i].Count == 0);
            }

            for (int i = 0; i < 20; i++)
            {
                list[i].Dispose();
            }
        }

        [TestMethod]
        public async Task Shortdelay()
        {
            _cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(500));

            await Task.Delay(50);

            Assert.IsTrue(_cache.TryGet("42", out int result));
            Assert.IsTrue(result == 42);
        }

        [TestMethod]
        public async Task TestWithDefaultJobInterval()
        {
            _cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(_cache.TryGet("42", out _));
            await Task.Delay(150);
            Assert.IsFalse(_cache.TryGet("42", out _));
        }

        [TestMethod]
        public void TestRemove()
        {
            _cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));
            _cache.Remove("42");
            Assert.IsFalse(_cache.TryGet("42", out _));
        }

        [TestMethod]
        public async Task TestGetOrAdd()
        {
            _cache.GetOrAdd("key", k => 1024, TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(_cache.TryGet("key", out int res) && res == 1024);
            await Task.Delay(110);

            Assert.IsFalse(_cache.TryGet("key", out _));
        }

        [TestMethod]
        public async Task TestGetOrAddWithArg()
        {
            _cache.GetOrAdd("key", (k, arg) => 1024 + arg.Length, TimeSpan.FromMilliseconds(100), "test123");
            Assert.IsTrue(_cache.TryGet("key", out int res) && res == 1031);

            //eviction
            await Task.Delay(110);
            Assert.IsFalse(_cache.TryGet("key", out _));
        }

        [TestMethod]
        public void TestClear()
        {
            _cache.GetOrAdd("key", k => 1024, TimeSpan.FromSeconds(100));

            _cache.Clear();

            Assert.IsTrue(!_cache.TryGet("key", out int res));
        }

        [TestMethod]
        public async Task Enumerator()
        {
            _cache.GetOrAdd("key", k => 1024, TimeSpan.FromMilliseconds(100));

            Assert.IsTrue(_cache.FirstOrDefault().Value == 1024);

            await Task.Delay(110);

            Assert.IsFalse(_cache.Any());
        }

        [TestMethod]
        public async Task TestTtlExtended()
        {
            _cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(300));

            await Task.Delay(50);
            Assert.IsTrue(_cache.TryGet("42", out int result));
            Assert.IsTrue(result == 42);

            _cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(300));

            await Task.Delay(250);

            Assert.IsTrue(_cache.TryGet("42", out int result2));
            Assert.IsTrue(result2 == 42);
        }

        [TestMethod]
        public async Task AddOrUpdate_ShouldBeAtomic()
        {
            var tasks = new Task[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    _cache.AddOrUpdate("5000", i, TimeSpan.FromMinutes(1));
                });
            }

            await Task.WhenAll(tasks);

            Assert.IsTrue(_cache.TryGet("5000", out var value));
            Assert.IsTrue(value >= 0 && value <= 100);
        }

        [TestMethod]
        public async Task TryGet_ShouldBeAtomic()
        {
            var tasks = new Task[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    _cache.TryGet("5000", out var _);
                });
            }

            await Task.WhenAll(tasks);

            // No exception should be thrown
        }

        [TestMethod]
        public async Task Remove_ShouldBeAtomic()
        {
            var tasks = new Task[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    _cache.Remove("5000");
                });
            }

            await Task.WhenAll(tasks);

            // No exception should be thrown
        }

        [TestMethod]
        public async Task Clear_ShouldBeAtomic()
        {
            var tasks = new Task[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    _cache.Clear();
                });
            }

            await Task.WhenAll(tasks);

            // No exception should be thrown
        }

        [TestMethod]
        public async Task HighConcurrencyTest()
        {
            var tasks = new Task[1000];

            for (int i = 0; i < tasks.Length; i++)
            {
                var localI = i.ToString();
                tasks[i] = Task.Run(() =>
                {
                    _cache.AddOrUpdate(localI, i, TimeSpan.FromMinutes(1));
                    if (_cache.TryGet(localI, out var value))
                    {
                        Assert.AreEqual(i, value);
                    }
                    _cache.Remove(localI);
                });
            }

            await Task.WhenAll(tasks);

            Assert.AreEqual(0, _cache.Count);
        }

        [TestMethod]
        public async Task EvictLFU_ShouldEvictLeastFrequentlyUsedItems_UnderHighConcurrency()
        {
            // Arrange
            var tasks = new Task[1000];
            var zipfianKeys = Enumerable.Range(0, 1000).Select(i => i.ToString()).ToList();
            Random rand = new();

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                var localI = i.ToString();
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        // Zipfian distributed items
                        foreach (var key in zipfianKeys)
                        {
                            var frequency = 1.0 / (zipfianKeys.IndexOf(key) + 1);
                            if (rand.NextDouble() < frequency)
                            {
                                _cache.AddOrUpdate(key, i, TimeSpan.FromMinutes(1));
                                _cache.TryGet(key, out _);
                            }
                        }
                    }
                });
            }
            await Task.WhenAll(tasks);

            // Assert
            // The frequently accessed items should not have been evicted
            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(_cache.TryGet(i.ToString(), out _));
            }

            // The infrequently accessed items should have been evicted
            for (int i = 100; i < 1000; i++)
            {
                Assert.IsFalse(_cache.TryGet(i.ToString(), out _));
            }
        }

        [TestMethod]
        public void TestAddOrUpdate_ShouldAddNewItemToWindowCache_WhenWindowCacheIsNotFull()
        {
            _cache.AddOrUpdate("key", 1024, TimeSpan.FromMinutes(1));

            Assert.IsTrue(_cache.TryGet("key", out int res) && res == 1024);
        }

        [TestMethod]
        public void TestAddOrUpdate_ShouldMoveItemToMainCache_WhenItemIsAlreadyInCache()
        {
            _cache.AddOrUpdate("key", 1024, TimeSpan.FromMinutes(1));
            _cache.AddOrUpdate("key", 1024, TimeSpan.FromMinutes(1));

            Assert.IsTrue(_cache.TryGet("key", out int res) && res == 1024);
        }

        [TestMethod]
        public void TestAddOrUpdate_ShouldReplaceRandomItemInWindowCache_WhenWindowCacheIsFull()
        {
            _cache.AddOrUpdate("key1", 1024, TimeSpan.FromMinutes(1));
            _cache.AddOrUpdate("key2", 1024, TimeSpan.FromMinutes(1));

            Assert.IsTrue(_cache.TryGet("key1", out _) ^ _cache.TryGet("key2", out _));
        }
    }
}