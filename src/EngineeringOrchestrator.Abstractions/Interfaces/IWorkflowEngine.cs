using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Graph;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Abstractions.Interfaces;

/// <summary>The workflow engine: validates a task DAG and executes it under policy/budget control.</summary>
public interface IWorkflowEngine
{
    GraphValidationResult Validate(WorkflowDefinition workflow);

    Task<WorkflowExecutionReport> ExecuteAsync(
        WorkflowDefinition workflow,
        WorkflowEngineOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Resumes a previously interrupted run using whatever state was last persisted for this
    /// workflow id; completed tasks are not re-executed. Behaves like <see cref="ExecuteAsync"/> if
    /// no prior state exists.</summary>
    Task<WorkflowExecutionReport> ResumeAsync(
        WorkflowDefinition workflow,
        WorkflowEngineOptions? options = null,
        CancellationToken cancellationToken = default);
}
