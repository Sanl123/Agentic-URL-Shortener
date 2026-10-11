using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Execution;
using EngineeringOrchestrator.Policies;
using EngineeringOrchestrator.Stores;
using EngineeringOrchestrator.Tools;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests;

public class WorkflowEngineCancellationTests
{
    private static WorkflowTaskDefinition Task(string id, params string[] dependsOn) => new()
    {
        Id = id,
        Name = id,
        DependsOn = dependsOn
    };

    [Fact]
    public async Task ExecuteAsync_WhenExternallyCancelled_StopsSafelyAndMarksCancelled()
    {
        var modelProvider = new DelayedConcurrencyModelProvider(TimeSpan.FromMilliseconds(500));
        var engine = new WorkflowEngine(
            modelProvider, new EchoToolRunner(), new AllowListToolExecutionPolicy(Array.Empty<string>()),
            new InMemoryArtifactStore(), new ScriptedApprovalService(), new InMemoryWorkflowStateStore());

        var workflow = new WorkflowDefinition
        {
            Id = "cancel-wf",
            Name = "Cancellable",
            Tasks = new[] { Task("a"), Task("b"), Task("c", "b") }
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var report = await engine.ExecuteAsync(workflow, cancellationToken: cts.Token);

        Assert.Equal(WorkflowStatus.Cancelled, report.FinalStatus);
        Assert.Contains(report.TaskReports, t => t.FinalStatus == WorkflowTaskStatus.Cancelled);
        Assert.Contains(report.Limitations, l => l.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceedingMaxTotalDuration_CancelsAsSafeStop()
    {
        var modelProvider = new DelayedConcurrencyModelProvider(TimeSpan.FromMilliseconds(500));
        var engine = new WorkflowEngine(
            modelProvider, new EchoToolRunner(), new AllowListToolExecutionPolicy(Array.Empty<string>()),
            new InMemoryArtifactStore(), new ScriptedApprovalService(), new InMemoryWorkflowStateStore());

        var workflow = new WorkflowDefinition { Id = "budget-wf", Name = "Budget", Tasks = new[] { Task("a"), Task("b") } };

        var report = await engine.ExecuteAsync(workflow, new WorkflowEngineOptions { MaxTotalDuration = TimeSpan.FromMilliseconds(50) });

        Assert.Equal(WorkflowStatus.Cancelled, report.FinalStatus);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidGraph_ReturnsFailedReportWithoutRunningAnyTask()
    {
        var harness = new WorkflowEngineTestHarness();
        var workflow = new WorkflowDefinition
        {
            Id = "invalid-wf",
            Name = "Invalid",
            Tasks = new[] { Task("a", "b"), Task("b", "a") }
        };

        var report = await harness.Engine.ExecuteAsync(workflow);

        Assert.Equal(WorkflowStatus.Failed, report.FinalStatus);
        Assert.Empty(report.TaskReports);
        Assert.Contains(report.Decisions, d => d.Contains("Cycle detected", StringComparison.OrdinalIgnoreCase));
    }
}
