using EngineeringOrchestrator.Abstractions.Approvals;

namespace EngineeringOrchestrator.Abstractions.Interfaces;

/// <summary>Gatekeeper for high-impact actions. The engine always awaits a decision before
/// proceeding with an at-or-above-threshold-risk task; it never infers approval from model output.</summary>
public interface IApprovalService
{
    Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken);
}
