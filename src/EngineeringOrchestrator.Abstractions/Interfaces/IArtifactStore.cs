using EngineeringOrchestrator.Abstractions.Artifacts;

namespace EngineeringOrchestrator.Abstractions.Interfaces;

/// <summary>Persists and retrieves artifacts produced during workflow execution.</summary>
public interface IArtifactStore
{
    Task SaveAsync(Artifact artifact, CancellationToken cancellationToken);

    Task<Artifact?> GetAsync(string artifactId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Artifact>> GetByTaskAsync(string workflowId, string taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Artifact>> GetByWorkflowAsync(string workflowId, CancellationToken cancellationToken);
}
