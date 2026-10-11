using System.Text.Json;
using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Interfaces;

namespace EngineeringOrchestrator.Stores;

/// <summary>
/// Persists artifacts as files under a run-specific directory: <c>{runDir}/artifacts/{id}.json</c>
/// for metadata plus <c>{id}.content</c> for the raw content, so a run's produced work products
/// survive process exit and are inspectable directly on disk.
/// </summary>
public sealed class FileArtifactStore : IArtifactStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FileArtifactStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(_directory, $"{artifact.Id}.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(artifact, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Artifact?> GetAsync(string artifactId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_directory, $"{artifactId}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Artifact>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<Artifact>> GetByTaskAsync(string workflowId, string taskId, CancellationToken cancellationToken)
    {
        var all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(a => a.WorkflowId == workflowId && a.TaskId == taskId).ToList();
    }

    public async Task<IReadOnlyList<Artifact>> GetByWorkflowAsync(string workflowId, CancellationToken cancellationToken)
    {
        var all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(a => a.WorkflowId == workflowId).ToList();
    }

    private async Task<List<Artifact>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var results = new List<Artifact>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var artifact = JsonSerializer.Deserialize<Artifact>(json, JsonOptions);
            if (artifact is not null)
            {
                results.Add(artifact);
            }
        }

        return results;
    }
}
