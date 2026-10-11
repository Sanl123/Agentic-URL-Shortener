using System.Collections.Concurrent;
using System.Text.Json;
using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Abstractions.Tools;

namespace EngineeringOrchestrator.Scenarios;

/// <summary>
/// A deterministic, scenario-keyed <see cref="IModelProvider"/>: no network access, no API key,
/// no randomness. Each recognized <see cref="ScenarioIds"/> value maps to a hardcoded task
/// decomposition and a hardcoded response per task id/attempt — this is a stand-in for what a real
/// LLM would reason about, not a demonstration of live model intelligence. Tests can override any
/// response via <see cref="OverrideTaskResponse"/>/<see cref="OverrideDecomposition"/> to inject
/// malformed plans, transient/permanent failures, or ambiguous decisions without touching this class.
/// </summary>
public sealed partial class ScenarioFakeModelProvider : IModelProvider
{
    private readonly string _scenarioId;
    private readonly ConcurrentDictionary<string, TaskExecutionResponse> _taskOverrides = new();
    private readonly ConcurrentQueue<TaskDecompositionResponse> _decompositionOverrides = new();
    private readonly List<string> _invocationLog = new();

    public ScenarioFakeModelProvider(string scenarioId)
    {
        _scenarioId = scenarioId;
    }

    /// <summary>Records every (taskId, attempt) this provider was asked to execute, and whether a
    /// scenario default or a test override answered it — without storing any secret/credential.</summary>
    public IReadOnlyList<string> InvocationLog => _invocationLog;

    public void OverrideTaskResponse(string taskId, TaskExecutionResponse response) => _taskOverrides[taskId] = response;

    public void OverrideDecomposition(TaskDecompositionResponse response) => _decompositionOverrides.Enqueue(response);

    public Task<TaskDecompositionResponse> DecomposeAsync(TaskDecompositionRequest request, CancellationToken cancellationToken)
    {
        if (_decompositionOverrides.TryDequeue(out var overridden))
        {
            _invocationLog.Add($"Decompose: test override used (context={request.Context}).");
            return Task.FromResult(overridden);
        }

        if (string.Equals(request.Context, "replan", StringComparison.OrdinalIgnoreCase))
        {
            _invocationLog.Add($"Decompose: replan plan for scenario '{_scenarioId}'.");
            return Task.FromResult(BuildReplanPlan());
        }

        _invocationLog.Add($"Decompose: initial plan for scenario '{_scenarioId}'.");
        return Task.FromResult(BuildInitialPlan());
    }

    public Task<TaskExecutionResponse> ExecuteTaskAsync(TaskExecutionRequest request, CancellationToken cancellationToken)
    {
        if (_taskOverrides.TryGetValue(request.Task.Id, out var overridden))
        {
            _invocationLog.Add($"Execute: test override used for task '{request.Task.Id}' (attempt {request.AttemptNumber}).");
            return Task.FromResult(overridden);
        }

        _invocationLog.Add($"Execute: scenario default used for task '{request.Task.Id}' (attempt {request.AttemptNumber}).");
        return Task.FromResult(BuildTaskResponse(request));
    }

    private TaskDecompositionResponse BuildInitialPlan() => _scenarioId switch
    {
        ScenarioIds.Greenfield => GreenfieldPlan(),
        ScenarioIds.Brownfield => BrownfieldInitialPlan(),
        ScenarioIds.Ambiguous => AmbiguousPlan(),
        _ => new TaskDecompositionResponse(
            new[] { new WorkflowTaskDefinition { Id = "unrecognized-scenario", Name = "Unrecognized scenario" } },
            $"No canned plan exists for scenario id '{_scenarioId}'.")
    };

    private TaskDecompositionResponse BuildReplanPlan() => _scenarioId switch
    {
        ScenarioIds.Brownfield => BrownfieldFixPlan(),
        _ => new TaskDecompositionResponse(Array.Empty<WorkflowTaskDefinition>(), "No replan plan defined for this scenario.")
    };

    private TaskExecutionResponse BuildTaskResponse(TaskExecutionRequest request) => _scenarioId switch
    {
        ScenarioIds.Greenfield => GreenfieldTaskResponse(request),
        ScenarioIds.Brownfield => BrownfieldTaskResponse(request),
        ScenarioIds.Ambiguous => AmbiguousTaskResponse(request),
        _ => Fail($"No canned response for scenario '{_scenarioId}' task '{request.Task.Id}'.")
    };

    private static TaskExecutionResponse Success(string output, IReadOnlyList<ProposedArtifact>? artifacts = null, ToolInvocation? tool = null) =>
        new(true, output, artifacts ?? Array.Empty<ProposedArtifact>(), null, FailureClassification.None, tool);

    private static TaskExecutionResponse Fail(string error, FailureClassification classification = FailureClassification.Permanent) =>
        new(false, null, Array.Empty<ProposedArtifact>(), error, classification);

    private static ToolInvocation WriteFiles(string filesJson) => new("write-files", new Dictionary<string, string> { ["files"] = filesJson });
    private static ToolInvocation DotnetBuild(string path) => new("dotnet-build", new Dictionary<string, string> { ["path"] = path });
    private static ToolInvocation DotnetTest(string path) => new("dotnet-test", new Dictionary<string, string> { ["path"] = path });
    private static ToolInvocation ListDirectory(string path = ".") => new("list-directory", new Dictionary<string, string> { ["path"] = path });
}
