using EngineeringOrchestrator.Abstractions.ModelProvider;

namespace EngineeringOrchestrator.Abstractions.Interfaces;

/// <summary>
/// Produces task decompositions and task execution outcomes. Implementations may call a live LLM;
/// tests use a deterministic fake so no API key or network access is required.
/// </summary>
public interface IModelProvider
{
    Task<TaskDecompositionResponse> DecomposeAsync(TaskDecompositionRequest request, CancellationToken cancellationToken);

    Task<TaskExecutionResponse> ExecuteTaskAsync(TaskExecutionRequest request, CancellationToken cancellationToken);
}
