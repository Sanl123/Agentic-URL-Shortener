using System.Collections.Concurrent;
using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.ModelProviders;

/// <summary>
/// A fully deterministic <see cref="IModelProvider"/> for tests and offline demos — no network
/// access or API key required. Behavior is driven entirely by scripted responses configured ahead
/// of time; nothing here is random or time-dependent.
/// </summary>
public sealed class DeterministicFakeModelProvider : IModelProvider
{
    private readonly ConcurrentDictionary<string, Queue<TaskExecutionResponse>> _scriptedTaskResponses = new();
    private readonly Queue<TaskDecompositionResponse> _scriptedDecompositions = new();

    /// <summary>
    /// Scripts the sequence of responses returned for a given task id. Responses are consumed in
    /// order as the engine retries; the last entry in the list repeats indefinitely once reached.
    /// </summary>
    public void ScriptTaskResponses(string taskId, params TaskExecutionResponse[] responses)
    {
        if (responses.Length == 0)
        {
            throw new ArgumentException("At least one response must be scripted.", nameof(responses));
        }

        _scriptedTaskResponses[taskId] = new Queue<TaskExecutionResponse>(responses);
    }

    /// <summary>Scripts the sequence of responses returned from <see cref="DecomposeAsync"/> (consumed in call order).</summary>
    public void ScriptDecomposition(params TaskDecompositionResponse[] responses)
    {
        foreach (var response in responses)
        {
            _scriptedDecompositions.Enqueue(response);
        }
    }

    public Task<TaskDecompositionResponse> DecomposeAsync(TaskDecompositionRequest request, CancellationToken cancellationToken)
    {
        lock (_scriptedDecompositions)
        {
            if (_scriptedDecompositions.Count > 0)
            {
                return Task.FromResult(_scriptedDecompositions.Dequeue());
            }
        }

        // Deterministic, content-free fallback: a single manual-review task so callers always get a valid DAG.
        var fallbackTask = new WorkflowTaskDefinition
        {
            Id = "manual-review",
            Name = "Manual review required",
            Description = $"No decomposition was scripted for requirement: {request.Requirement}",
            RiskLevel = RiskLevel.Medium
        };

        return Task.FromResult(new TaskDecompositionResponse(new[] { fallbackTask }, "Fallback: no decomposition was scripted."));
    }

    public Task<TaskExecutionResponse> ExecuteTaskAsync(TaskExecutionRequest request, CancellationToken cancellationToken)
    {
        if (_scriptedTaskResponses.TryGetValue(request.Task.Id, out var queue))
        {
            lock (queue)
            {
                if (queue.Count > 1)
                {
                    return Task.FromResult(queue.Dequeue());
                }

                if (queue.Count == 1)
                {
                    return Task.FromResult(queue.Peek());
                }
            }
        }

        var defaultResponse = new TaskExecutionResponse(
            true,
            $"Deterministic fake completed task '{request.Task.Id}' (attempt {request.AttemptNumber}).",
            Array.Empty<ProposedArtifact>(),
            null,
            FailureClassification.None);

        return Task.FromResult(defaultResponse);
    }
}
