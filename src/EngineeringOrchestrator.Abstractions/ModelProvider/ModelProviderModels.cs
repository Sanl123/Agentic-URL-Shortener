using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Abstractions.Tools;

namespace EngineeringOrchestrator.Abstractions.ModelProvider;

public sealed record TaskDecompositionRequest(string Requirement, string? Context = null);

public sealed record TaskDecompositionResponse(IReadOnlyList<WorkflowTaskDefinition> Tasks, string Rationale);

public sealed record TaskExecutionRequest(
    WorkflowTaskDefinition Task,
    IReadOnlyList<Artifact> UpstreamArtifacts,
    int AttemptNumber);

public sealed record TaskExecutionResponse(
    bool Success,
    string? Output,
    IReadOnlyList<ProposedArtifact> Artifacts,
    string? ErrorMessage,
    FailureClassification Classification,
    ToolInvocation? RequestedTool = null);
