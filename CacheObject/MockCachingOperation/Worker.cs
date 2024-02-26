using Microsoft.Extensions.Hosting;

namespace MockCachingOperation
{
    public class Worker(MockCachingOperation mockCachingOperation) : BackgroundService
    {
        private readonly MockCachingOperation _mockCachingOperation = mockCachingOperation;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Console.WriteLine($"Beginning caching operation: {DateTime.Now}\n");
                await _mockCachingOperation.StartAsync(stoppingToken);
            }
        }
    }

}
