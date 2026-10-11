namespace EngineeringOrchestrator.Scenarios;

/// <summary>
/// The scenario-level outcome, distinct from the engine's <see cref="Abstractions.Execution.WorkflowStatus"/>:
/// a run that correctly pauses for clarification on ambiguous/under-specified requirements is a
/// successful safe pause, not a failure, even though some tasks end up Skipped/Blocked at the
/// engine level.
/// </summary>
public enum ScenarioOutcome
{
    /// <summary>All required tasks completed and acceptance criteria were met.</summary>
    Success,

    /// <summary>The run correctly identified unresolved material decisions and safely paused
    /// implementation pending human clarification/approval — not a failure.</summary>
    NeedsClarification,

    /// <summary>A required task failed, was blocked by a genuine failure, or acceptance criteria were not met.</summary>
    Failed
}
