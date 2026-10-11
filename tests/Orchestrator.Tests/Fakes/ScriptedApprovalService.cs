using System.Collections.Concurrent;
using EngineeringOrchestrator.Abstractions.Approvals;
using EngineeringOrchestrator.Abstractions.Interfaces;

namespace Orchestrator.Tests.Fakes;

/// <summary>Deterministic, scriptable <see cref="IApprovalService"/> for tests: approve/reject per task id.</summary>
public sealed class ScriptedApprovalService : IApprovalService
{
    private readonly ConcurrentDictionary<string, bool> _decisions = new();
    private readonly ConcurrentBag<ApprovalRequest> _requests = new();
    private readonly bool _defaultApproval;

    public ScriptedApprovalService(bool defaultApproval = true)
    {
        _defaultApproval = defaultApproval;
    }

    public IReadOnlyCollection<ApprovalRequest> Requests => _requests;

    public void Script(string taskId, bool approve) => _decisions[taskId] = approve;

    public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        _requests.Add(request);
        var approved = _decisions.TryGetValue(request.TaskId, out var scripted) ? scripted : _defaultApproval;
        var decision = new ApprovalDecision(approved, "test-reviewer", approved ? "Approved for test." : "Rejected for test.", DateTimeOffset.UtcNow);
        return Task.FromResult(decision);
    }
}
