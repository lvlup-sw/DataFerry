using lvlup.DataFerry.Caches;

namespace lvlup.DataFerry.Tests
{
    [TestClass]
    public class FastMemCacheTests
    {
        private FastMemCache<int, int> _cache = default!;

        [TestInitialize]
        public void Setup()
        {
            _cache = new FastMemCache<int, int>();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cache.Dispose();
        }

        public static Task<ParallelLoopResult> RunConcurrently(int numThreads, Action action)
        {
            return Task.Run(() => Parallel.For(0, numThreads, _ => action()));
        }

        [TestMethod]
        public async Task TestGetSetCleanup()
        {
            var _cache = new FastMemCache<int, int>(cleanupJobInterval: 200);
            _cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(_cache.TryGet(42, out int v));
            Assert.IsTrue(v == 42);

            await Task.Delay(300);
            Assert.IsTrue(_cache.Count == 0); //cleanup job has run?
        }

        [TestMethod]
        public async Task TestEviction()
        {
            var list = new List<FastMemCache<int, int>>();
            for (int i = 0; i < 20; i++)
            {
                var cache = new FastMemCache<int, int>(cleanupJobInterval: 200);
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
            var cache = new FastMemCache<int, int>();
            cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(500));

            await Task.Delay(50);

            Assert.IsTrue(cache.TryGet(42, out int result));
            Assert.IsTrue(result == 42);
        }

        [TestMethod]
        public async Task TestWithDefaultJobInterval()
        {
            var _cache2 = new FastMemCache<string, int>();
            _cache2.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(_cache2.TryGet("42", out _));
            await Task.Delay(150);
            Assert.IsFalse(_cache2.TryGet("42", out _));
        }

        [TestMethod]
        public void TestRemove()
        {
            var cache = new FastMemCache<string, int>();
            cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));
            cache.Remove("42");
            Assert.IsFalse(cache.TryGet("42", out _));
        }

        [TestMethod]
        public async Task TestGetOrAdd()
        {
            var cache = new FastMemCache<string, int>();
            cache.GetOrAdd("key", k => 1024, TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(cache.TryGet("key", out int res) && res == 1024);
            await Task.Delay(110);

            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [TestMethod]
        public async Task TestGetOrAddWithArg()
        {
            var cache = new FastMemCache<string, int>();
            cache.GetOrAdd("key", (k, arg) => 1024 + arg.Length, TimeSpan.FromMilliseconds(100), "test123");
            Assert.IsTrue(cache.TryGet("key", out int res) && res == 1031);

            //eviction
            await Task.Delay(110);
            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [TestMethod]
        public void TestClear()
        {
            var cache = new FastMemCache<string, int>();
            cache.GetOrAdd("key", k => 1024, TimeSpan.FromSeconds(100));

            cache.Clear();

            Assert.IsTrue(!cache.TryGet("key", out int res));
        }

        // Flaky
        [TestMethod]
        public async Task TestGetOrAddAtomicNess()
        {
            int i = 0;

            _cache.GetOrAdd(42, 42, TimeSpan.FromMilliseconds(100));

            await Task.Delay(110); // wait for tha value to expire

            await RunConcurrently(20, () => {
                _cache.GetOrAdd(42, k => { return ++i; }, TimeSpan.FromSeconds(1));
            });

            // test that only the first value was added
            _cache.TryGet(42, out i);
            Assert.IsTrue(i == 1, i.ToString());
        }

        [TestMethod]
        public async Task Enumerator()
        {
            var cache = new FastMemCache<string, int>();
            cache.GetOrAdd("key", k => 1024, TimeSpan.FromMilliseconds(100));

            Assert.IsTrue(cache.FirstOrDefault().Value == 1024);

            await Task.Delay(110);

            Assert.IsFalse(cache.Any());
        }

        [TestMethod]
        public async Task TestTtlExtended()
        {
            var _cache = new FastMemCache<int, int>();
            _cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(300));

            await Task.Delay(50);
            Assert.IsTrue(_cache.TryGet(42, out int result));
            Assert.IsTrue(result == 42);

            _cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(300));

            await Task.Delay(250);

            Assert.IsTrue(_cache.TryGet(42, out int result2));
            Assert.IsTrue(result2 == 42);
        }
    }
}