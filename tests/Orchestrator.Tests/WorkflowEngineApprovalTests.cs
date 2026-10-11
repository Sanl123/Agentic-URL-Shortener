using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace Orchestrator.Tests;

public class WorkflowEngineApprovalTests
{
    [Fact]
    public async Task ExecuteAsync_HighRiskTask_RequestsApprovalBeforeRunning()
    {
        var harness = new WorkflowEngineTestHarness(defaultApproval: true);
        var workflow = new WorkflowDefinition
        {
            Id = "approval-wf",
            Name = "Approval required",
            Tasks = new[] { new WorkflowTaskDefinition { Id = "deploy", Name = "Deploy to prod", RiskLevel = RiskLevel.High } }
        };

        var report = await harness.Engine.ExecuteAsync(workflow);

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        Assert.Single(harness.ApprovalService.Requests);
        Assert.Equal("deploy", harness.ApprovalService.Requests.Single().TaskId);
        Assert.Contains(report.Decisions, d => d.Contains("approved by", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_RejectedApproval_SkipsTaskAndBlocksDependents()
    {
        var harness = new WorkflowEngineTestHarness();
        harness.ApprovalService.Script("deploy", approve: false);

        var workflow = new WorkflowDefinition
        {
            Id = "rejected-wf",
            Name = "Rejected",
            Tasks = new[]
            {
                new WorkflowTaskDefinition { Id = "deploy", Name = "Deploy to prod", RiskLevel = RiskLevel.Critical },
                new WorkflowTaskDefinition { Id = "notify", Name = "Notify stakeholders", DependsOn = new[] { "deploy" } }
            }
        };

        var report = await harness.Engine.ExecuteAsync(workflow);

        Assert.Equal(WorkflowStatus.Failed, report.FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Skipped, report.TaskReports.Single(t => t.TaskId == "deploy").FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Blocked, report.TaskReports.Single(t => t.TaskId == "notify").FinalStatus);
    }

    [Fact]
    public async Task ExecuteAsync_LowRiskTask_NeverRequestsApproval()
    {
        var harness = new WorkflowEngineTestHarness();
        var workflow = new WorkflowDefinition
        {
            Id = "low-risk-wf",
            Name = "Low risk",
            Tasks = new[] { new WorkflowTaskDefinition { Id = "lint", Name = "Run linter", RiskLevel = RiskLevel.Low } }
        };

        var report = await harness.Engine.ExecuteAsync(workflow);

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        Assert.Empty(harness.ApprovalService.Requests);
    }
}
