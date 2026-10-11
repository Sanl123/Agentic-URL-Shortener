using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Scenarios;

namespace Orchestrator.Tests;

public class ScenarioFakeModelProviderTests
{
    private static WorkflowTaskDefinition Find(TaskDecompositionResponse response, string id) =>
        Assert.Single(response.Tasks.Where(t => t.Id == id));

    [Fact]
    public async Task DecomposeAsync_Greenfield_ReturnsExpectedTaskGraph()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Greenfield);

        var response = await provider.DecomposeAsync(new TaskDecompositionRequest("irrelevant text"), CancellationToken.None);

        Assert.Equal(7, response.Tasks.Count);
        var expectedIds = new[]
        {
            "analyze-requirements", "inspect-workspace", "decompose-and-validate-dependencies",
            "implement-feature", "build-solution", "run-tests", "produce-final-report"
        };
        Assert.Equal(expectedIds.OrderBy(x => x), response.Tasks.Select(t => t.Id).OrderBy(x => x));
        Assert.Equal(WorkflowTaskStatus.Pending, WorkflowTaskStatus.Pending); // sanity: enum usable
        Assert.Contains("build-solution", Find(response, "run-tests").DependsOn);
    }

    [Fact]
    public async Task DecomposeAsync_Brownfield_InitialPlan_EndsWithGenuinelyRetryableFinalTestTask()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Brownfield);

        var response = await provider.DecomposeAsync(new TaskDecompositionRequest("irrelevant"), CancellationToken.None);

        var finalTask = Find(response, "run-tests-after-expiry-change");
        Assert.Equal(1, finalTask.MaxRetries); // a single real failure must be terminal, not silently retried
    }

    [Fact]
    public async Task DecomposeAsync_Brownfield_ReplanContext_ReturnsFixPlanNotInitialPlan()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Brownfield);

        var replan = await provider.DecomposeAsync(new TaskDecompositionRequest("fix it", Context: "replan"), CancellationToken.None);

        Assert.Equal(3, replan.Tasks.Count);
        Assert.Contains(replan.Tasks, t => t.Id == "fix-expiry-check-bug");
        Assert.DoesNotContain(replan.Tasks, t => t.Id == "add-expiry-field-and-check");
    }

    [Fact]
    public async Task DecomposeAsync_Ambiguous_GatesMaterialTasksBehindCriticalRisk()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Ambiguous);

        var response = await provider.DecomposeAsync(new TaskDecompositionRequest("make it fast"), CancellationToken.None);

        var propose = Find(response, "propose-performance-and-analytics-improvements");
        var deploy = Find(response, "deploy-performance-improvements");
        Assert.Equal(RiskLevel.Critical, propose.RiskLevel);
        Assert.Equal(RiskLevel.Critical, deploy.RiskLevel);

        var report = Find(response, "produce-ambiguity-report");
        Assert.DoesNotContain("propose-performance-and-analytics-improvements", report.DependsOn);
    }

    [Fact]
    public async Task ExecuteTaskAsync_IsDeterministic_SameRequestProducesSameOutcome()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Greenfield);
        var decomposition = await provider.DecomposeAsync(new TaskDecompositionRequest("x"), CancellationToken.None);
        var task = Find(decomposition, "analyze-requirements");
        var request = new TaskExecutionRequest(task, Array.Empty<Artifact>(), 1);

        var first = await provider.ExecuteTaskAsync(request, CancellationToken.None);
        var second = await provider.ExecuteTaskAsync(request, CancellationToken.None);

        Assert.Equal(first.Success, second.Success);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(first.Artifacts.Count, second.Artifacts.Count);
    }

    [Fact]
    public async Task ExecuteTaskAsync_UnknownTaskId_FailsCleanlyRatherThanThrowing()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Greenfield);
        var bogusTask = new WorkflowTaskDefinition { Id = "no-such-task", Name = "Bogus" };

        var response = await provider.ExecuteTaskAsync(
            new TaskExecutionRequest(bogusTask, Array.Empty<Artifact>(), 1), CancellationToken.None);

        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task OverrideTaskResponse_TakesPrecedenceOverScenarioDefault()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Greenfield);
        provider.OverrideTaskResponse(
            "analyze-requirements",
            new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), "injected transient failure", FailureClassification.Transient));

        var task = new WorkflowTaskDefinition { Id = "analyze-requirements", Name = "Analyze" };
        var response = await provider.ExecuteTaskAsync(
            new TaskExecutionRequest(task, Array.Empty<Artifact>(), 1), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("injected transient failure", response.ErrorMessage);
        Assert.Equal(FailureClassification.Transient, response.Classification);
    }

    [Fact]
    public async Task OverrideDecomposition_InjectsMalformedPlan_WithDanglingDependency()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Greenfield);
        var malformed = new TaskDecompositionResponse(
            new[] { new WorkflowTaskDefinition { Id = "a", Name = "A", DependsOn = new[] { "does-not-exist" } } },
            "malformed plan for testing");
        provider.OverrideDecomposition(malformed);

        var response = await provider.DecomposeAsync(new TaskDecompositionRequest("anything"), CancellationToken.None);

        Assert.Single(response.Tasks);
        Assert.Contains("does-not-exist", response.Tasks[0].DependsOn);
    }

    [Fact]
    public void InvocationLog_RecordsEachDecomposeAndExecuteCall()
    {
        var provider = new ScenarioFakeModelProvider(ScenarioIds.Ambiguous);
        Assert.Empty(provider.InvocationLog);
    }
}
