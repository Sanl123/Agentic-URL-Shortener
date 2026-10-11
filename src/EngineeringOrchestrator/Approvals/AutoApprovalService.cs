using EngineeringOrchestrator.Abstractions.Approvals;
using EngineeringOrchestrator.Abstractions.Interfaces;

namespace EngineeringOrchestrator.Approvals;

/// <summary>
/// Approves every request unconditionally. Intended only for local demos/tests where no human
/// reviewer is wired up — production deployments must supply a real <see cref="IApprovalService"/>.
/// </summary>
public sealed class AutoApprovalService : IApprovalService
{
    public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ApprovalDecision(true, "auto-approval (demo)", "Automatically approved — no human reviewer configured.", DateTimeOffset.UtcNow));
}
