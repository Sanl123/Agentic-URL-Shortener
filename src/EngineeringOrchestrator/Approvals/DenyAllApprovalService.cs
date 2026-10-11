using EngineeringOrchestrator.Abstractions.Approvals;
using EngineeringOrchestrator.Abstractions.Interfaces;

namespace EngineeringOrchestrator.Approvals;

/// <summary>
/// Rejects every request unconditionally. Used where no human clarification/approval has been
/// recorded for a material, high-impact decision — the mirror image of <see cref="AutoApprovalService"/>.
/// </summary>
public sealed class DenyAllApprovalService : IApprovalService
{
    public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ApprovalDecision(
            false,
            "deny-all (no clarification recorded)",
            "Rejected: a material, high-impact decision requires human clarification/approval that has not been recorded.",
            DateTimeOffset.UtcNow));
}
