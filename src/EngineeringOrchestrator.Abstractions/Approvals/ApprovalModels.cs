using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Abstractions.Approvals;

/// <summary>Raised by the engine when a task's risk level meets or exceeds the configured approval threshold.</summary>
public sealed record ApprovalRequest(string WorkflowId, string TaskId, RiskLevel RiskLevel, string Reason);

public sealed record ApprovalDecision(bool IsApproved, string DecidedBy, string? Reason, DateTimeOffset DecidedAtUtc);
