using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace Orchestrator.Tests;

public class WorkflowEngineResumeTests
{
    private static WorkflowTaskDefinition Task(string id, params string[] dependsOn) => new()
    {
        Id = id,
        Name = id,
        DependsOn = dependsOn
    };

    [Fact]
    public async Task ResumeAsync_AfterPermanentFailure_DoesNotReRunCompletedTasksAndRetriesTheFailedOne()
    {
        var harness = new WorkflowEngineTestHarness();
        harness.ModelProvider.ScriptTaskResponses("a", new TaskExecutionResponse(true, "a done", Array.Empty<ProposedArtifact>(), null, FailureClassification.None));
        harness.ModelProvider.ScriptTaskResponses("b", new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), "transient infra issue", FailureClassification.Permanent));

        var workflow = new WorkflowDefinition
        {
            Id = "resume-wf",
            Name = "Resume me",
            Tasks = new[] { Task("a"), Task("b", "a") }
        };

        var firstReport = await harness.Engine.ExecuteAsync(workflow);
        Assert.Equal(WorkflowStatus.Failed, firstReport.FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Completed, firstReport.TaskReports.Single(t => t.TaskId == "a").FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Failed, firstReport.TaskReports.Single(t => t.TaskId == "b").FinalStatus);

        // Simulate the underlying issue being fixed before the operator resumes the run.
        harness.ModelProvider.ScriptTaskResponses("b", new TaskExecutionResponse(true, "b done after fix", Array.Empty<ProposedArtifact>(), null, FailureClassification.None));

        var resumedReport = await harness.Engine.ResumeAsync(workflow);

        Assert.Equal(WorkflowStatus.Completed, resumedReport.FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Completed, resumedReport.TaskReports.Single(t => t.TaskId == "a").FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Completed, resumedReport.TaskReports.Single(t => t.TaskId == "b").FinalStatus);

        var history = await harness.StateStore.GetHistoryAsync("resume-wf", CancellationToken.None);
        var taskAStarts = history.Count(e => e.TaskId == "a" && e.EventType == WorkflowEventType.TaskStarted);
        var taskBStarts = history.Count(e => e.TaskId == "b" && e.EventType == WorkflowEventType.TaskStarted);

        Assert.Equal(1, taskAStarts); // never re-run — it was already Completed
        Assert.Equal(2, taskBStarts); // original failed attempt + the resumed attempt
    }

    [Fact]
    public async Task ResumeAsync_WithNoPriorState_BehavesLikeExecuteAsync()
    {
        var harness = new WorkflowEngineTestHarness();
        harness.ModelProvider.ScriptTaskResponses("a", new TaskExecutionResponse(true, "ok", Array.Empty<ProposedArtifact>(), null, FailureClassification.None));

        var workflow = new WorkflowDefinition { Id = "fresh-resume-wf", Name = "Fresh", Tasks = new[] { Task("a") } };

        var report = await harness.Engine.ResumeAsync(workflow);

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Completed, report.TaskReports.Single().FinalStatus);
    }

    [Fact]
    public async Task ResumeAsync_PreservesAttemptCountSoRetryBudgetCannotBeBypassed()
    {
        var harness = new WorkflowEngineTestHarness();
        var transientFailure = new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), "still broken", FailureClassification.Transient);
        harness.ModelProvider.ScriptTaskResponses("a", transientFailure, transientFailure);

        var workflow = new WorkflowDefinition { Id = "resume-budget-wf", Name = "Resume budget", Tasks = new[] { Task("a") } };
        var options = new WorkflowEngineOptions { RetryDelay = TimeSpan.FromMilliseconds(1) };

        var firstReport = await harness.Engine.ExecuteAsync(workflow, options);
        // MaxRetries defaults to 1, so the single attempt already exhausted the budget.
        Assert.Equal(WorkflowTaskStatus.Failed, firstReport.TaskReports.Single().FinalStatus);
        Assert.Equal(1, firstReport.TaskReports.Single().Attempts);

        harness.ModelProvider.ScriptTaskResponses("a", transientFailure);
        var resumedReport = await harness.Engine.ResumeAsync(workflow, options);

        // Resuming re-attempts once more (attempt count continues from 1 to 2) and fails again —
        // it must not silently reset the attempt counter back to 0 and grant unlimited extra tries.
        Assert.Equal(WorkflowTaskStatus.Failed, resumedReport.TaskReports.Single().FinalStatus);
        Assert.Equal(2, resumedReport.TaskReports.Single().Attempts);
    }
}
