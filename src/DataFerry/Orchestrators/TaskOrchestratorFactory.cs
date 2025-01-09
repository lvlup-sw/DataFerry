using lvlup.DataFerry.Orchestrators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lvlup.DataFerry.Caches;

public class TaskOrchestratorFactory(IServiceProvider serviceProvider) : ITaskOrchestratorFactory
{
    public TaskOrchestrator Create(TaskOrchestrator.TaskOrchestratorFeatures features, int workerCount)
    {
        // Resolve the logger using the service provider
        var logger = serviceProvider.GetRequiredService<ILogger<TaskOrchestrator>>();
        return new TaskOrchestrator(logger, features, workerCount);
    }
}