using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Abstractions.Execution;

/// <summary>Per-task summary included in the final workflow report.</summary>
public sealed record TaskExecutionReport(
    string TaskId,
    WorkflowTaskStatus FinalStatus,
    int Attempts,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Error,
    IReadOnlyList<string> ArtifactIds);

/// <summary>
/// The final engineering report for one workflow run: outcomes, the decisions that shaped them,
/// and the evidence/risk/assumption/limitation narrative required for human review.
/// </summary>
public sealed record WorkflowExecutionReport(
    string WorkflowId,
    WorkflowStatus FinalStatus,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<TaskExecutionReport> TaskReports,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Limitations);
