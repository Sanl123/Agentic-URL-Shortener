using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Execution;
using EngineeringOrchestrator.Policies;
using EngineeringOrchestrator.Stores;
using EngineeringOrchestrator.Tools;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests;

public class WorkflowEngineTimeoutTests
{
    [Fact]
    public async Task ExecuteAsync_TaskExceedingItsTimeout_IsTreatedAsTransientFailureAndRetried()
    {
        var modelProvider = new SlowFirstAttemptModelProvider(TimeSpan.FromMilliseconds(300));
        var engine = new WorkflowEngine(
            modelProvider, new EchoToolRunner(), new AllowListToolExecutionPolicy(Array.Empty<string>()),
            new InMemoryArtifactStore(), new ScriptedApprovalService(), new InMemoryWorkflowStateStore());

        var workflow = new WorkflowDefinition
        {
            Id = "timeout-wf",
            Name = "Per-task timeout",
            Tasks = new[]
            {
                new WorkflowTaskDefinition
                {
                    Id = "slow-task",
                    Name = "Slow task",
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromMilliseconds(50)
                }
            }
        };

        var report = await engine.ExecuteAsync(workflow, new WorkflowEngineOptions { RetryDelay = TimeSpan.FromMilliseconds(1) });

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        var taskReport = report.TaskReports.Single();
        Assert.Equal(WorkflowTaskStatus.Completed, taskReport.FinalStatus);
        Assert.Equal(2, taskReport.Attempts); // attempt 1 timed out, attempt 2 (fast) succeeded
        Assert.Contains(report.Decisions, d => d.Contains("retried", StringComparison.OrdinalIgnoreCase) && d.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_TaskExceedingTimeoutWithNoRetriesLeft_FailsPermanently()
    {
        var modelProvider = new SlowFirstAttemptModelProvider(TimeSpan.FromMilliseconds(300));
        var engine = new WorkflowEngine(
            modelProvider, new EchoToolRunner(), new AllowListToolExecutionPolicy(Array.Empty<string>()),
            new InMemoryArtifactStore(), new ScriptedApprovalService(), new InMemoryWorkflowStateStore());

        var workflow = new WorkflowDefinition
        {
            Id = "timeout-no-retry-wf",
            Name = "Per-task timeout, no retries",
            Tasks = new[]
            {
                new WorkflowTaskDefinition { Id = "slow-task", Name = "Slow task", MaxRetries = 1, Timeout = TimeSpan.FromMilliseconds(50) }
            }
        };

        var report = await engine.ExecuteAsync(workflow);

        var taskReport = report.TaskReports.Single();
        Assert.Equal(WorkflowTaskStatus.Failed, taskReport.FinalStatus);
        Assert.Equal(1, taskReport.Attempts);
        Assert.Contains("timed out", taskReport.Error, StringComparison.OrdinalIgnoreCase);
    }
}
