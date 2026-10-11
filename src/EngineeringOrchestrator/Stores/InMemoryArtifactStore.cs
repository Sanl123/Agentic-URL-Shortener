using System.Collections.Concurrent;
using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Interfaces;

namespace EngineeringOrchestrator.Stores;

/// <summary>Thread-safe in-memory <see cref="IArtifactStore"/>.</summary>
public sealed class InMemoryArtifactStore : IArtifactStore
{
    private readonly ConcurrentDictionary<string, Artifact> _artifacts = new();

    public Task SaveAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        _artifacts[artifact.Id] = artifact;
        return Task.CompletedTask;
    }

    public Task<Artifact?> GetAsync(string artifactId, CancellationToken cancellationToken) =>
        Task.FromResult(_artifacts.TryGetValue(artifactId, out var artifact) ? artifact : null);

    public Task<IReadOnlyList<Artifact>> GetByTaskAsync(string workflowId, string taskId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Artifact> matches = _artifacts.Values
            .Where(a => a.WorkflowId == workflowId && a.TaskId == taskId)
            .ToList();
        return Task.FromResult(matches);
    }

    public Task<IReadOnlyList<Artifact>> GetByWorkflowAsync(string workflowId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Artifact> matches = _artifacts.Values
            .Where(a => a.WorkflowId == workflowId)
            .ToList();
        return Task.FromResult(matches);
    }
}
