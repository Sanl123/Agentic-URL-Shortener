using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Execution;
using EngineeringOrchestrator.Policies;
using EngineeringOrchestrator.Stores;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests;

public class WorkflowEngineSchedulingTests
{
    private static WorkflowTaskDefinition Task(string id, params string[] dependsOn) => new()
    {
        Id = id,
        Name = id,
        DependsOn = dependsOn
    };

    private static TaskExecutionResponse Success(string taskId) =>
        new(true, $"{taskId} done", Array.Empty<ProposedArtifact>(), null, FailureClassification.None);

    [Fact]
    public async Task ExecuteAsync_WithLinearChain_RunsTasksInDependencyOrder()
    {
        var harness = new WorkflowEngineTestHarness();

        harness.ModelProvider.ScriptTaskResponses("a", Success("a"));
        harness.ModelProvider.ScriptTaskResponses("b", Success("b"));
        harness.ModelProvider.ScriptTaskResponses("c", Success("c"));

        var workflow = new WorkflowDefinition
        {
            Id = "linear",
            Name = "Linear",
            Tasks = new[] { Task("a"), Task("b", "a"), Task("c", "b") }
        };

        var report = await harness.Engine.ExecuteAsync(workflow);

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        var history = await harness.StateStore.GetHistoryAsync("linear", CancellationToken.None);
        var startOrder = history
            .Where(e => e.EventType == WorkflowEventType.TaskStarted)
            .Select(e => e.TaskId)
            .ToList();

        Assert.Equal(new[] { "a", "b", "c" }, startOrder);
    }

    [Fact]
    public async Task ExecuteAsync_WithDiamond_RunsIndependentBranchesInParallel()
    {
        var modelProvider = new DelayedConcurrencyModelProvider(TimeSpan.FromMilliseconds(150));
        var artifactStore = new InMemoryArtifactStore();
        var stateStore = new InMemoryWorkflowStateStore();
        var approvalService = new ScriptedApprovalService();
        var toolPolicy = new AllowListToolExecutionPolicy(Array.Empty<string>());
        var toolRunner = new EngineeringOrchestrator.Tools.EchoToolRunner();
        var engine = new WorkflowEngine(modelProvider, toolRunner, toolPolicy, artifactStore, approvalService, stateStore);

        var workflow = new WorkflowDefinition
        {
            Id = "diamond",
            Name = "Diamond",
            Tasks = new[] { Task("a"), Task("b", "a"), Task("c", "a"), Task("d", "b", "c") }
        };

        var report = await engine.ExecuteAsync(workflow, new WorkflowEngineOptions { MaxDegreeOfParallelism = 4 });

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        Assert.True(modelProvider.MaxObservedConcurrency >= 2, $"Expected b/c to overlap; max observed concurrency was {modelProvider.MaxObservedConcurrency}.");
    }

    [Fact]
    public async Task ExecuteAsync_WithParallelismLimitOfOne_NeverRunsTasksConcurrently()
    {
        var modelProvider = new DelayedConcurrencyModelProvider(TimeSpan.FromMilliseconds(50));
        var artifactStore = new InMemoryArtifactStore();
        var stateStore = new InMemoryWorkflowStateStore();
        var approvalService = new ScriptedApprovalService();
        var toolPolicy = new AllowListToolExecutionPolicy(Array.Empty<string>());
        var toolRunner = new EngineeringOrchestrator.Tools.EchoToolRunner();
        var engine = new WorkflowEngine(modelProvider, toolRunner, toolPolicy, artifactStore, approvalService, stateStore);

        var workflow = new WorkflowDefinition
        {
            Id = "fan-out",
            Name = "Fan-out",
            Tasks = new[] { Task("a"), Task("b", "a"), Task("c", "a"), Task("d", "a") }
        };

        var report = await engine.ExecuteAsync(workflow, new WorkflowEngineOptions { MaxDegreeOfParallelism = 1 });

        Assert.Equal(WorkflowStatus.Completed, report.FinalStatus);
        Assert.Equal(1, modelProvider.MaxObservedConcurrency);
    }
}
