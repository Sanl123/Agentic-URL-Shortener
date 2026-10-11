using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Abstractions.Interfaces;

/// <summary>
/// Persists workflow and task state so a run's progress and history survive process restarts and
/// can be inspected independently of the engine that produced them.
/// </summary>
public interface IWorkflowStateStore
{
    Task SaveWorkflowStatusAsync(string workflowId, WorkflowStatus status, CancellationToken cancellationToken);

    Task<WorkflowStatus?> GetWorkflowStatusAsync(string workflowId, CancellationToken cancellationToken);

    Task SaveTaskStateAsync(string workflowId, TaskState state, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, TaskState>> GetTaskStatesAsync(string workflowId, CancellationToken cancellationToken);

    Task AppendEventAsync(WorkflowExecutionEvent executionEvent, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowExecutionEvent>> GetHistoryAsync(string workflowId, CancellationToken cancellationToken);
}
