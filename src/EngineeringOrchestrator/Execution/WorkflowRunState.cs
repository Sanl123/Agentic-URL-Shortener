using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Graph;

namespace EngineeringOrchestrator.Execution;

/// <summary>Mutable bag of everything one <c>ExecuteAsync</c> call needs; never shared across runs.</summary>
internal sealed class WorkflowRunState
{
    public required WorkflowDefinition Workflow { get; init; }
    public required WorkflowGraph Graph { get; init; }
    public required WorkflowEngineOptions Options { get; init; }
    public required Dictionary<string, TaskState> States { get; init; }
    public required SemaphoreSlim Semaphore { get; init; }
    public required CancellationToken Token { get; init; }
    public required List<string> Decisions { get; init; }
    public required List<string> Risks { get; init; }

    public required IModelProvider ModelProvider { get; init; }
    public required IToolRunner ToolRunner { get; init; }
    public required IToolExecutionPolicy ToolPolicy { get; init; }
    public required IArtifactStore ArtifactStore { get; init; }
    public required IApprovalService ApprovalService { get; init; }
    public required IWorkflowStateStore StateStore { get; init; }
}
