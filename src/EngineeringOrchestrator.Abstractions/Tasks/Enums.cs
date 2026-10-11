namespace EngineeringOrchestrator.Abstractions.Tasks;

/// <summary>Impact level of a task; drives whether an approval checkpoint is required before execution.</summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>Lifecycle state of a single task within a workflow run.</summary>
public enum WorkflowTaskStatus
{
    /// <summary>Not yet started; dependencies may or may not be satisfied yet.</summary>
    Pending,

    /// <summary>Dependencies satisfied, risk level requires sign-off before execution can begin.</summary>
    AwaitingApproval,

    Running,
    Completed,

    /// <summary>Exhausted retries (or a permanent failure) — terminal.</summary>
    Failed,

    /// <summary>Never ran because an upstream dependency did not complete successfully — terminal.</summary>
    Blocked,

    /// <summary>Never ran because an approval was rejected — terminal.</summary>
    Skipped,

    Cancelled
}

/// <summary>Overall status of a workflow run.</summary>
public enum WorkflowStatus
{
    Draft,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>How a task failure should be treated by the scheduler's retry logic.</summary>
public enum FailureClassification
{
    /// <summary>No failure has occurred.</summary>
    None,

    /// <summary>Likely to succeed on retry (e.g. a flaky tool call); bounded retries apply.</summary>
    Transient,

    /// <summary>Will not succeed on retry without a re-plan (e.g. an acceptance-criteria violation).</summary>
    Permanent
}
