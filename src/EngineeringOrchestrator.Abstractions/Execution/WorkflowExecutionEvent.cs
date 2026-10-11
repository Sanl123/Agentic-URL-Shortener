namespace EngineeringOrchestrator.Abstractions.Execution;

/// <summary>Kinds of events recorded in a workflow's append-only execution history.</summary>
public enum WorkflowEventType
{
    WorkflowStarted,
    TaskStarted,
    TaskCompleted,
    TaskFailed,
    TaskRetried,
    TaskBlocked,
    TaskSkipped,
    ApprovalRequested,
    ApprovalGranted,
    ApprovalRejected,
    WorkflowCompleted,
    WorkflowFailed,
    WorkflowCancelled,
    Replanned
}

/// <summary>A single immutable entry in a workflow's execution history (decision lineage).</summary>
public sealed record WorkflowExecutionEvent(
    string WorkflowId,
    string? TaskId,
    WorkflowEventType EventType,
    string Details,
    DateTimeOffset TimestampUtc);
