using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace lvlup.DataFerry.Tests.Benchmarks
{
    public class BenchmarkDebugger
    {
        [Config(typeof(Config))]
        public class Config : ManualConfig
        {
            public Config()
            {
                AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
            }
        }

        public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(BenchmarkDebugger).Assembly).Run(args, new DebugInProcessConfig());
    }
}
