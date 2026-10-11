using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Abstractions.Execution;

/// <summary>Mutable, persistable runtime state for a single task within one workflow run.</summary>
public sealed class TaskState
{
    public required string TaskId { get; init; }
    public WorkflowTaskStatus Status { get; set; } = WorkflowTaskStatus.Pending;
    public int Attempt { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
    public FailureClassification LastFailureClassification { get; set; } = FailureClassification.None;
    public List<string> ArtifactIds { get; init; } = new();

    public TaskState Clone() => new()
    {
        TaskId = TaskId,
        Status = Status,
        Attempt = Attempt,
        StartedAtUtc = StartedAtUtc,
        CompletedAtUtc = CompletedAtUtc,
        LastError = LastError,
        LastFailureClassification = LastFailureClassification,
        ArtifactIds = new List<string>(ArtifactIds)
    };
}
