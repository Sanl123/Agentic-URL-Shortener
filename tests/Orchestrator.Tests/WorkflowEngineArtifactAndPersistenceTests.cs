using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Abstractions.Tools;

namespace Orchestrator.Tests;

public class WorkflowEngineArtifactAndPersistenceTests
{
    private static WorkflowTaskDefinition Task(string id, params string[] dependsOn) => new()
    {
        Id = id,
        Name = id,
        DependsOn = dependsOn
    };

    [Fact]
    public async Task ExecuteAsync_TaskProducingArtifacts_PersistsThemAttributedToTaskAndWorkflow()
    {
        var harness = new WorkflowEngineTestHarness();
        harness.ModelProvider.ScriptTaskResponses(
            "design",
            new TaskExecutionResponse(
                true, "design complete",
                new[] { new ProposedArtifact("design-doc.md", "text/markdown", "# Design\n...") },
                null, FailureClassification.None));

        var workflow = new WorkflowDefinition { Id = "artifact-wf", Name = "Artifacts", Tasks = new[] { Task("design") } };

        var report = await harness.Engine.ExecuteAsync(workflow);

        var taskReport = report.TaskReports.Single(t => t.TaskId == "design");
        Assert.Single(taskReport.ArtifactIds);

        var artifacts = await harness.ArtifactStore.GetByTaskAsync("artifact-wf", "design", CancellationToken.None);
        var artifact = Assert.Single(artifacts);
        Assert.Equal("design-doc.md", artifact.Name);
        Assert.Equal("artifact-wf", artifact.WorkflowId);
        Assert.Equal("design", artifact.TaskId);
    }

    [Fact]
    public async Task ExecuteAsync_DownstreamTask_ReceivesUpstreamArtifactsAsContext()
    {
        var harness = new WorkflowEngineTestHarness();
        harness.ModelProvider.ScriptTaskResponses(
            "design",
            new TaskExecutionResponse(true, "ok", new[] { new ProposedArtifact("spec.md", "text/markdown", "spec content") }, null, FailureClassification.None));

        harness.ModelProvider.ScriptTaskResponses("implement", new TaskExecutionResponse(true, "ok", Array.Empty<ProposedArtifact>(), null, FailureClassification.None));

        var workflow = new WorkflowDefinition
        {
            Id = "upstream-wf",
            Name = "Upstream context",
            Tasks = new[] { Task("design"), Task("implement", "design") }
        };

        await harness.Engine.ExecuteAsync(workflow);

        var artifacts = await harness.ArtifactStore.GetByWorkflowAsync("upstream-wf", CancellationToken.None);
        Assert.Single(artifacts);
        Assert.Equal("design", artifacts[0].TaskId);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsWorkflowStatusAndTaskStatesInStateStore()
    {
        var harness = new WorkflowEngineTestHarness();
        harness.ModelProvider.ScriptTaskResponses("a", new TaskExecutionResponse(true, "ok", Array.Empty<ProposedArtifact>(), null, FailureClassification.None));

        var workflow = new WorkflowDefinition { Id = "persist-wf", Name = "Persisted", Tasks = new[] { Task("a") } };

        await harness.Engine.ExecuteAsync(workflow);

        var status = await harness.StateStore.GetWorkflowStatusAsync("persist-wf", CancellationToken.None);
        Assert.Equal(WorkflowStatus.Completed, status);

        var states = await harness.StateStore.GetTaskStatesAsync("persist-wf", CancellationToken.None);
        Assert.Equal(WorkflowTaskStatus.Completed, states["a"].Status);

        var history = await harness.StateStore.GetHistoryAsync("persist-wf", CancellationToken.None);
        Assert.Contains(history, e => e.EventType == WorkflowEventType.WorkflowStarted);
        Assert.Contains(history, e => e.EventType == WorkflowEventType.WorkflowCompleted);
        Assert.Contains(history, e => e.EventType == WorkflowEventType.TaskCompleted);
    }

    [Fact]
    public async Task ExecuteAsync_DisallowedTool_IsRejectedWithoutRunningIt()
    {
        var harness = new WorkflowEngineTestHarness(allowedTools: new[] { "echo" });
        harness.ModelProvider.ScriptTaskResponses(
            "shell",
            new TaskExecutionResponse(
                true, null, Array.Empty<ProposedArtifact>(), null, FailureClassification.None,
                RequestedTool: new ToolInvocation("run-arbitrary-shell", new Dictionary<string, string> { ["cmd"] = "rm -rf /" })));

        var workflow = new WorkflowDefinition { Id = "tool-wf", Name = "Tool policy", Tasks = new[] { Task("shell") } };

        var report = await harness.Engine.ExecuteAsync(workflow);

        Assert.Equal(WorkflowStatus.Failed, report.FinalStatus);
        var taskReport = report.TaskReports.Single(t => t.TaskId == "shell");
        Assert.Equal(WorkflowTaskStatus.Failed, taskReport.FinalStatus);
        Assert.Contains(report.Decisions, d => d.Contains("Blocked tool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_AllowedTool_RunsSuccessfully()
    {
        var harness = new WorkflowEngineTestHarness(allowedTools: new[] { "echo" });
        harness.ModelProvider.ScriptTaskResponses(
            "format-check",
            new TaskExecutionResponse(
                true, null, Array.Empty<ProposedArtifact>(), null, FailureClassification.None,
                RequestedTool: new ToolInvocation("echo", new Dictionary<string, string> { ["message"] = "hello" })));

        var workflow = new WorkflowDefinition { Id = "allowed-tool-wf", Name = "Allowed tool", Tasks = new[] { Task("format-check") } };

        var report = await harness.Engine.ExecuteAsync(workflow);

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        Assert.Contains(report.Decisions, d => d.Contains("ran tool 'echo'", StringComparison.OrdinalIgnoreCase));
    }
}
