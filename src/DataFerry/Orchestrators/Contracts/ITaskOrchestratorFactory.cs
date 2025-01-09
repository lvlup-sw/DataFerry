using lvlup.DataFerry.Orchestrators;

namespace lvlup.DataFerry.Caches.Contracts;

public interface ITaskOrchestratorFactory
{
    TaskOrchestrator Create(TaskOrchestrator.TaskOrchestratorFeatures features = TaskOrchestrator.TaskOrchestratorFeatures.None, int workerCount = 2);
}