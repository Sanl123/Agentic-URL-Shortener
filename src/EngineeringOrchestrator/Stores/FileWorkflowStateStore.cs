using System.Text.Json;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Stores;

/// <summary>
/// Persists workflow status, task state, and history events as files under a run-specific
/// directory so a run's progress genuinely survives the process and can be inspected or resumed
/// later — not merely held in memory for the lifetime of one CLI invocation.
/// </summary>
public sealed class FileWorkflowStateStore : IWorkflowStateStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _eventLock = new();
    private readonly SemaphoreSlim _taskStateLock = new(1, 1);

    public FileWorkflowStateStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveWorkflowStatusAsync(string workflowId, WorkflowStatus status, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(StatusPath(workflowId), status.ToString(), cancellationToken).ConfigureAwait(false);

    public async Task<WorkflowStatus?> GetWorkflowStatusAsync(string workflowId, CancellationToken cancellationToken)
    {
        var path = StatusPath(workflowId);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return Enum.TryParse<WorkflowStatus>(text, out var status) ? status : null;
    }

    public async Task SaveTaskStateAsync(string workflowId, TaskState state, CancellationToken cancellationToken)
    {
        // Multiple tasks run concurrently and each calls this; serialize the read-modify-write
        // cycle so concurrent writers never race on the same per-workflow state file.
        await _taskStateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await LoadTaskStatesAsync(workflowId, cancellationToken).ConfigureAwait(false);
            all[state.TaskId] = state.Clone();
            await File.WriteAllTextAsync(TaskStatesPath(workflowId), JsonSerializer.Serialize(all, JsonOptions), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _taskStateLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, TaskState>> GetTaskStatesAsync(string workflowId, CancellationToken cancellationToken)
    {
        var dict = await LoadTaskStatesAsync(workflowId, cancellationToken).ConfigureAwait(false);
        return dict;
    }

    public Task AppendEventAsync(WorkflowExecutionEvent executionEvent, CancellationToken cancellationToken)
    {
        // Serialized under a lock: multiple tasks may append concurrently from different threads.
        lock (_eventLock)
        {
            var line = JsonSerializer.Serialize(executionEvent);
            File.AppendAllText(HistoryPath(executionEvent.WorkflowId), line + Environment.NewLine);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<WorkflowExecutionEvent>> GetHistoryAsync(string workflowId, CancellationToken cancellationToken)
    {
        var path = HistoryPath(workflowId);
        if (!File.Exists(path))
        {
            return Array.Empty<WorkflowExecutionEvent>();
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<WorkflowExecutionEvent>(l))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    private async Task<Dictionary<string, TaskState>> LoadTaskStatesAsync(string workflowId, CancellationToken cancellationToken)
    {
        var path = TaskStatesPath(workflowId);
        if (!File.Exists(path))
        {
            return new Dictionary<string, TaskState>();
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Dictionary<string, TaskState>>(json, JsonOptions) ?? new Dictionary<string, TaskState>();
    }

    private string SafeName(string workflowId) => string.Join("_", workflowId.Split(Path.GetInvalidFileNameChars()));
    private string StatusPath(string workflowId) => Path.Combine(_directory, $"{SafeName(workflowId)}.status.txt");
    private string TaskStatesPath(string workflowId) => Path.Combine(_directory, $"{SafeName(workflowId)}.tasks.json");
    private string HistoryPath(string workflowId) => Path.Combine(_directory, $"{SafeName(workflowId)}.history.jsonl");
}
