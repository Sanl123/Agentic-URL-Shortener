using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace Orchestrator.Tests;

public class WorkflowEngineFailureTests
{
    private static WorkflowTaskDefinition Task(string id, int maxRetries = 1, params string[] dependsOn) => new()
    {
        Id = id,
        Name = id,
        DependsOn = dependsOn,
        MaxRetries = maxRetries
    };

    [Fact]
    public async Task ExecuteAsync_TransientFailureThenSuccess_RetriesAndCompletes()
    {
        var harness = new WorkflowEngineTestHarness();
        var transientFailure = new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), "flaky tool call", FailureClassification.Transient);
        var success = new TaskExecutionResponse(true, "ok", Array.Empty<ProposedArtifact>(), null, FailureClassification.None);
        harness.ModelProvider.ScriptTaskResponses("a", transientFailure, transientFailure, success);

        var workflow = new WorkflowDefinition { Id = "retry-wf", Name = "Retry", Tasks = new[] { Task("a", maxRetries: 3) } };

        var report = await harness.Engine.ExecuteAsync(workflow, new WorkflowEngineOptions { RetryDelay = TimeSpan.FromMilliseconds(1) });

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        var taskReport = report.TaskReports.Single(t => t.TaskId == "a");
        Assert.Equal(WorkflowTaskStatus.Completed, taskReport.FinalStatus);
        Assert.Equal(3, taskReport.Attempts);
    }

    [Fact]
    public async Task ExecuteAsync_ExhaustedRetries_FailsPermanentlyAndBlocksDependents()
    {
        var harness = new WorkflowEngineTestHarness();
        var transientFailure = new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), "still flaky", FailureClassification.Transient);
        harness.ModelProvider.ScriptTaskResponses("a", transientFailure, transientFailure);
        harness.ModelProvider.ScriptTaskResponses("b", new TaskExecutionResponse(true, "should not run", Array.Empty<ProposedArtifact>(), null, FailureClassification.None));

        var workflow = new WorkflowDefinition
        {
            Id = "exhausted-wf",
            Name = "Exhausted retries",
            Tasks = new[] { Task("a", maxRetries: 2), Task("b", dependsOn: new[] { "a" }) }
        };

        var report = await harness.Engine.ExecuteAsync(workflow, new WorkflowEngineOptions { RetryDelay = TimeSpan.FromMilliseconds(1) });

        Assert.Equal(WorkflowStatus.Failed, report.FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Failed, report.TaskReports.Single(t => t.TaskId == "a").FinalStatus);
        Assert.Equal(WorkflowTaskStatus.Blocked, report.TaskReports.Single(t => t.TaskId == "b").FinalStatus);
        Assert.Contains(report.Risks, r => r.Contains("'a'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_PermanentFailure_DoesNotRetry()
    {
        var harness = new WorkflowEngineTestHarness();
        harness.ModelProvider.ScriptTaskResponses(
            "a",
            new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), "acceptance criteria not met", FailureClassification.Permanent));

        var workflow = new WorkflowDefinition { Id = "permanent-wf", Name = "Permanent failure", Tasks = new[] { Task("a", maxRetries: 5) } };

        var report = await harness.Engine.ExecuteAsync(workflow);

        var taskReport = report.TaskReports.Single(t => t.TaskId == "a");
        Assert.Equal(WorkflowTaskStatus.Failed, taskReport.FinalStatus);
        Assert.Equal(1, taskReport.Attempts); // never retried despite MaxRetries=5
    }

    [Fact]
    public async Task ReplanFailedTasksAsync_ForFailedTask_AsksModelForReplacement()
    {
        var harness = new WorkflowEngineTestHarness();
        harness.ModelProvider.ScriptTaskResponses(
            "a",
            new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), "bad approach", FailureClassification.Permanent));

        var workflow = new WorkflowDefinition { Id = "replan-wf", Name = "Needs replan", Tasks = new[] { Task("a") } };
        var report = await harness.Engine.ExecuteAsync(workflow);
        Assert.Equal(WorkflowStatus.Failed, report.FinalStatus);

        var replacementTask = new WorkflowTaskDefinition { Id = "a-v2", Name = "Retry with different approach" };
        harness.ModelProvider.ScriptDecomposition(new TaskDecompositionResponse(new[] { replacementTask }, "Use a different approach."));

        var replanned = await harness.Engine.ReplanFailedTasksAsync(workflow, report, CancellationToken.None);

        Assert.Single(replanned.Tasks);
        Assert.Equal("a-v2", replanned.Tasks[0].Id);
        Assert.True(harness.Engine.Validate(replanned).IsValid);
    }
}
