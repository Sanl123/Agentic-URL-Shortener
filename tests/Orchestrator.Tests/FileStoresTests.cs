using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Stores;

namespace Orchestrator.Tests;

public class FileStoresTests : IDisposable
{
    private readonly string _root;

    public FileStoresTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sw-file-stores-tests-" + Guid.NewGuid().ToString("n"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task FileArtifactStore_SaveThenGet_RoundTripsFullContent()
    {
        var store = new FileArtifactStore(Path.Combine(_root, "artifacts"));
        var artifact = new Artifact
        {
            Id = "art-1",
            WorkflowId = "wf-1",
            TaskId = "task-1",
            Name = "note.md",
            ContentType = "text/markdown",
            Content = "# Hello",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await store.SaveAsync(artifact, CancellationToken.None);
        var loaded = await store.GetAsync("art-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("# Hello", loaded!.Content);
    }

    [Fact]
    public async Task FileArtifactStore_GetByTaskAndByWorkflow_FilterCorrectly()
    {
        var store = new FileArtifactStore(Path.Combine(_root, "artifacts"));
        await store.SaveAsync(new Artifact { Id = "1", WorkflowId = "wf-a", TaskId = "t1", Name = "x" }, CancellationToken.None);
        await store.SaveAsync(new Artifact { Id = "2", WorkflowId = "wf-a", TaskId = "t2", Name = "y" }, CancellationToken.None);
        await store.SaveAsync(new Artifact { Id = "3", WorkflowId = "wf-b", TaskId = "t1", Name = "z" }, CancellationToken.None);

        var byTask = await store.GetByTaskAsync("wf-a", "t1", CancellationToken.None);
        var byWorkflow = await store.GetByWorkflowAsync("wf-a", CancellationToken.None);

        Assert.Single(byTask);
        Assert.Equal("1", byTask[0].Id);
        Assert.Equal(2, byWorkflow.Count);
    }

    [Fact]
    public async Task FileWorkflowStateStore_SaveAndGetWorkflowStatus_RoundTrips()
    {
        var store = new FileWorkflowStateStore(Path.Combine(_root, "state"));

        await store.SaveWorkflowStatusAsync("wf-1", WorkflowStatus.Running, CancellationToken.None);
        var status = await store.GetWorkflowStatusAsync("wf-1", CancellationToken.None);

        Assert.Equal(WorkflowStatus.Running, status);
    }

    [Fact]
    public async Task FileWorkflowStateStore_ConcurrentSaveTaskState_DoesNotThrowAndKeepsAllTasks()
    {
        // Regression test: concurrent SaveTaskStateAsync calls for different tasks in the same
        // workflow previously raced on the shared per-workflow state file ("file in use by another
        // process"). This must complete cleanly and every task's state must be retained.
        var store = new FileWorkflowStateStore(Path.Combine(_root, "state"));
        const string workflowId = "wf-concurrent";
        var taskIds = Enumerable.Range(0, 20).Select(i => $"task-{i}").ToList();

        var saves = taskIds.Select(id => store.SaveTaskStateAsync(
            workflowId,
            new TaskState { TaskId = id, Status = WorkflowTaskStatus.Completed },
            CancellationToken.None));

        await Task.WhenAll(saves);

        var states = await store.GetTaskStatesAsync(workflowId, CancellationToken.None);
        Assert.Equal(taskIds.Count, states.Count);
        Assert.All(taskIds, id => Assert.True(states.ContainsKey(id)));
    }

    [Fact]
    public async Task FileWorkflowStateStore_AppendEventThenGetHistory_RoundTrips()
    {
        var store = new FileWorkflowStateStore(Path.Combine(_root, "state"));
        var evt = new WorkflowExecutionEvent("wf-1", "task-1", WorkflowEventType.TaskCompleted, "done", DateTimeOffset.UtcNow);

        await store.AppendEventAsync(evt, CancellationToken.None);
        var history = await store.GetHistoryAsync("wf-1", CancellationToken.None);

        Assert.Single(history);
        Assert.Equal(WorkflowEventType.TaskCompleted, history[0].EventType);
    }
}
