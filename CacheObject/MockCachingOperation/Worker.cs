using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MockCachingOperation
{
    public class Worker(MockCachingOperation mockCachingOperation) : BackgroundService
    {
        private readonly MockCachingOperation _mockCachingOperation = mockCachingOperation;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Run(() => Console.WriteLine($"Beginning caching operation: {DateTime.Now}\n"), stoppingToken);
                await _mockCachingOperation.StartAsync(stoppingToken);
            }
        }
    }

}
