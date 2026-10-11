namespace EngineeringOrchestrator.Scenarios;

/// <summary>Structured ambiguity-analysis output for the ambiguous-requirements scenario. Serialized
/// as an artifact's JSON content — this is plain data the fake provider returns, never free text.</summary>
public sealed record AmbiguityAnalysis(
    string RequirementSummary,
    IReadOnlyList<string> UnresolvedQuestions,
    IReadOnlyList<string> UnapprovedAssumptions,
    string ImpactIfAssumed,
    string RiskIfAssumed,
    string ApprovalStatus,
    string PauseReason);
