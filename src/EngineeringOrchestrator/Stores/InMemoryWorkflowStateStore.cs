using System.Collections.Concurrent;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Stores;

/// <summary>
/// Thread-safe in-memory <see cref="IWorkflowStateStore"/>. Suitable for tests and single-process
/// demos; a durable (e.g. EF Core/SQL) implementation can replace it without touching the engine.
/// </summary>
public sealed class InMemoryWorkflowStateStore : IWorkflowStateStore
{
    private readonly ConcurrentDictionary<string, WorkflowStatus> _workflowStatuses = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TaskState>> _taskStates = new();
    private readonly ConcurrentDictionary<string, List<WorkflowExecutionEvent>> _history = new();

    public Task SaveWorkflowStatusAsync(string workflowId, WorkflowStatus status, CancellationToken cancellationToken)
    {
        _workflowStatuses[workflowId] = status;
        return Task.CompletedTask;
    }

    public Task<WorkflowStatus?> GetWorkflowStatusAsync(string workflowId, CancellationToken cancellationToken) =>
        Task.FromResult(_workflowStatuses.TryGetValue(workflowId, out var status) ? status : (WorkflowStatus?)null);

    public Task SaveTaskStateAsync(string workflowId, TaskState state, CancellationToken cancellationToken)
    {
        var tasks = _taskStates.GetOrAdd(workflowId, _ => new ConcurrentDictionary<string, TaskState>());
        tasks[state.TaskId] = state.Clone();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, TaskState>> GetTaskStatesAsync(string workflowId, CancellationToken cancellationToken)
    {
        if (!_taskStates.TryGetValue(workflowId, out var tasks))
        {
            return Task.FromResult<IReadOnlyDictionary<string, TaskState>>(new Dictionary<string, TaskState>());
        }

        IReadOnlyDictionary<string, TaskState> snapshot = tasks.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
        return Task.FromResult(snapshot);
    }

    public Task AppendEventAsync(WorkflowExecutionEvent executionEvent, CancellationToken cancellationToken)
    {
        var events = _history.GetOrAdd(executionEvent.WorkflowId, _ => new List<WorkflowExecutionEvent>());
        lock (events)
        {
            events.Add(executionEvent);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowExecutionEvent>> GetHistoryAsync(string workflowId, CancellationToken cancellationToken)
    {
        if (!_history.TryGetValue(workflowId, out var events))
        {
            return Task.FromResult<IReadOnlyList<WorkflowExecutionEvent>>(Array.Empty<WorkflowExecutionEvent>());
        }

        lock (events)
        {
            return Task.FromResult<IReadOnlyList<WorkflowExecutionEvent>>(events.ToList());
        }
    }
}
