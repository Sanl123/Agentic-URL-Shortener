using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Graph;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Graph;

namespace EngineeringOrchestrator.Execution;

/// <summary>
/// Dependency-aware DAG scheduler with bounded parallelism, retries, approval gates, cancellation,
/// and execution budgets. Independent of the URL shortener projects — it has no reference to them
/// and is never required to process a redirect.
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine
{
    private static readonly WorkflowTaskStatus[] NonCompletedTerminalStatuses =
    {
        WorkflowTaskStatus.Failed,
        WorkflowTaskStatus.Blocked,
        WorkflowTaskStatus.Skipped,
        WorkflowTaskStatus.Cancelled
    };

    private readonly IModelProvider _modelProvider;
    private readonly IToolRunner _toolRunner;
    private readonly IToolExecutionPolicy _toolPolicy;
    private readonly IArtifactStore _artifactStore;
    private readonly IApprovalService _approvalService;
    private readonly IWorkflowStateStore _stateStore;

    public WorkflowEngine(
        IModelProvider modelProvider,
        IToolRunner toolRunner,
        IToolExecutionPolicy toolPolicy,
        IArtifactStore artifactStore,
        IApprovalService approvalService,
        IWorkflowStateStore stateStore)
    {
        _modelProvider = modelProvider;
        _toolRunner = toolRunner;
        _toolPolicy = toolPolicy;
        _artifactStore = artifactStore;
        _approvalService = approvalService;
        _stateStore = stateStore;
    }

    public GraphValidationResult Validate(WorkflowDefinition workflow) =>
        WorkflowGraph.Validate(workflow, out _);

    public Task<WorkflowExecutionReport> ExecuteAsync(
        WorkflowDefinition workflow,
        WorkflowEngineOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(workflow, options, isResume: false, cancellationToken);

    /// <summary>
    /// Resumes a previously interrupted (e.g. crashed) run of this workflow id using whatever state
    /// <see cref="IWorkflowStateStore"/> last persisted. Tasks already <see cref="WorkflowTaskStatus.Completed"/>
    /// are not re-executed; every other task (including ones that were mid-flight, failed, or blocked)
    /// is given a fresh attempt, preserving its prior attempt count so the retry budget cannot be
    /// bypassed by repeated resumes. If no prior state exists, this behaves exactly like <see cref="ExecuteAsync"/>.
    /// </summary>
    public Task<WorkflowExecutionReport> ResumeAsync(
        WorkflowDefinition workflow,
        WorkflowEngineOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(workflow, options, isResume: true, cancellationToken);

    private async Task<WorkflowExecutionReport> ExecuteCoreAsync(
        WorkflowDefinition workflow,
        WorkflowEngineOptions? options,
        bool isResume,
        CancellationToken cancellationToken)
    {
        options ??= WorkflowEngineOptions.Default;
        var startedAtUtc = DateTimeOffset.UtcNow;

        var validation = WorkflowGraph.Validate(workflow, out var graph);
        if (!validation.IsValid || graph is null)
        {
            await _stateStore.SaveWorkflowStatusAsync(workflow.Id, WorkflowStatus.Failed, CancellationToken.None).ConfigureAwait(false);
            return new WorkflowExecutionReport(
                workflow.Id,
                WorkflowStatus.Failed,
                startedAtUtc,
                DateTimeOffset.UtcNow,
                Array.Empty<TaskExecutionReport>(),
                validation.Errors,
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "Workflow failed graph validation; no tasks were scheduled." });
        }

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.MaxTotalDuration is { } maxDuration)
        {
            budgetCts.CancelAfter(maxDuration);
        }

        var states = isResume
            ? await BuildResumedStatesAsync(workflow.Id, graph, cancellationToken).ConfigureAwait(false)
            : graph.Tasks.ToDictionary(t => t.Id, t => new TaskState { TaskId = t.Id });

        var run = new WorkflowRunState
        {
            Workflow = workflow,
            Graph = graph,
            Options = options,
            States = states,
            Semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism),
            Token = budgetCts.Token,
            Decisions = new List<string>(),
            Risks = new List<string>(),
            ModelProvider = _modelProvider,
            ToolRunner = _toolRunner,
            ToolPolicy = _toolPolicy,
            ArtifactStore = _artifactStore,
            ApprovalService = _approvalService,
            StateStore = _stateStore
        };

        foreach (var state in run.States.Values)
        {
            await _stateStore.SaveTaskStateAsync(workflow.Id, state, cancellationToken).ConfigureAwait(false);
        }

        await _stateStore.SaveWorkflowStatusAsync(workflow.Id, WorkflowStatus.Running, cancellationToken).ConfigureAwait(false);
        await RecordEventAsync(
            run, null, WorkflowEventType.WorkflowStarted,
            isResume ? "Workflow execution resumed from persisted state." : "Workflow execution started.").ConfigureAwait(false);

        await RunSchedulingLoopAsync(run).ConfigureAwait(false);

        var finalStatus = DetermineFinalStatus(run);
        await _stateStore.SaveWorkflowStatusAsync(workflow.Id, finalStatus, CancellationToken.None).ConfigureAwait(false);
        await RecordEventAsync(
            run,
            null,
            finalStatus switch
            {
                WorkflowStatus.Completed => WorkflowEventType.WorkflowCompleted,
                WorkflowStatus.Cancelled => WorkflowEventType.WorkflowCancelled,
                _ => WorkflowEventType.WorkflowFailed
            },
            $"Workflow finished with status {finalStatus}.").ConfigureAwait(false);

        return BuildReport(run, finalStatus, startedAtUtc);
    }

    /// <summary>
    /// Loads persisted task state for <paramref name="workflowId"/> and converts it into the
    /// starting point for a resumed run: completed tasks stay completed; everything else restarts
    /// as <see cref="WorkflowTaskStatus.Pending"/> with its attempt count preserved.
    /// </summary>
    private async Task<Dictionary<string, TaskState>> BuildResumedStatesAsync(
        string workflowId, WorkflowGraph graph, CancellationToken cancellationToken)
    {
        var persisted = await _stateStore.GetTaskStatesAsync(workflowId, cancellationToken).ConfigureAwait(false);

        return graph.Tasks.ToDictionary(t => t.Id, t =>
        {
            if (!persisted.TryGetValue(t.Id, out var previous))
            {
                return new TaskState { TaskId = t.Id };
            }

            if (previous.Status == WorkflowTaskStatus.Completed)
            {
                return previous.Clone();
            }

            return new TaskState
            {
                TaskId = t.Id,
                Status = WorkflowTaskStatus.Pending,
                Attempt = previous.Attempt
            };
        });
    }

    /// <summary>
    /// Asks the model provider to propose replacement tasks for every permanently failed task in
    /// <paramref name="report"/>, returning a brand-new, independently executable workflow.
    /// Returns an empty-task workflow if nothing failed.
    /// </summary>
    public async Task<WorkflowDefinition> ReplanFailedTasksAsync(
        WorkflowDefinition originalWorkflow,
        WorkflowExecutionReport report,
        CancellationToken cancellationToken)
    {
        var failedTasks = report.TaskReports.Where(t => t.FinalStatus == WorkflowTaskStatus.Failed).ToList();
        var replacementTasks = new List<WorkflowTaskDefinition>();

        foreach (var taskReport in failedTasks)
        {
            var originalTask = originalWorkflow.Tasks.First(t => t.Id == taskReport.TaskId);
            var requirement =
                $"Re-plan failed task '{originalTask.Name}' (id={originalTask.Id}). " +
                $"Original description: {originalTask.Description}. Failure reason: {taskReport.Error}.";

            var decomposition = await _modelProvider
                .DecomposeAsync(new TaskDecompositionRequest(requirement, Context: "replan"), cancellationToken)
                .ConfigureAwait(false);

            replacementTasks.AddRange(decomposition.Tasks);
        }

        return new WorkflowDefinition
        {
            Id = $"{originalWorkflow.Id}-replan-{Guid.NewGuid():n}",
            Name = $"{originalWorkflow.Name} (re-plan)",
            Tasks = replacementTasks
        };
    }

    private async Task RunSchedulingLoopAsync(WorkflowRunState run)
    {
        var running = new Dictionary<string, Task>();

        while (true)
        {
            await PropagateBlockedCascadesAsync(run).ConfigureAwait(false);

            if (run.States.Values.All(s => IsTerminal(s.Status)) && running.Count == 0)
            {
                break;
            }

            if (!run.Token.IsCancellationRequested)
            {
                var readyIds = run.States.Values
                    .Where(s => s.Status == WorkflowTaskStatus.Pending)
                    .Select(s => s.TaskId)
                    .Where(id => !running.ContainsKey(id))
                    .Where(id => run.Graph.GetTask(id).DependsOn.All(dep => run.States[dep].Status == WorkflowTaskStatus.Completed))
                    .ToList();

                foreach (var id in readyIds)
                {
                    running[id] = RunTaskLifecycleAsync(run, id);
                }
            }

            if (running.Count == 0)
            {
                // Cancellation stopped new launches and nothing is in flight; whatever is left
                // never got a chance to start.
                foreach (var state in run.States.Values.Where(s => s.Status == WorkflowTaskStatus.Pending))
                {
                    state.Status = WorkflowTaskStatus.Cancelled;
                    await PersistAndRecordAsync(run, state, WorkflowEventType.WorkflowCancelled, "Cancelled before it started.").ConfigureAwait(false);
                }

                break;
            }

            var completed = await Task.WhenAny(running.Values).ConfigureAwait(false);
            var completedId = running.First(kv => kv.Value == completed).Key;
            running.Remove(completedId);
            await completed.ConfigureAwait(false); // observe any exception rather than letting it go unhandled
        }
    }

    private async Task RunTaskLifecycleAsync(WorkflowRunState run, string taskId)
    {
        var definition = run.Graph.GetTask(taskId);
        var state = run.States[taskId];

        try
        {
            await run.Semaphore.WaitAsync(run.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            state.Status = WorkflowTaskStatus.Cancelled;
            await PersistAndRecordAsync(run, state, WorkflowEventType.WorkflowCancelled, "Cancelled while waiting for an execution slot.").ConfigureAwait(false);
            return;
        }

        try
        {
            if (run.Token.IsCancellationRequested)
            {
                state.Status = WorkflowTaskStatus.Cancelled;
                await PersistAndRecordAsync(run, state, WorkflowEventType.WorkflowCancelled, "Cancelled before it started.").ConfigureAwait(false);
                return;
            }

            if (definition.RiskLevel >= run.Options.ApprovalRequiredAtOrAbove)
            {
                var approved = await RequestApprovalAsync(run, definition, state).ConfigureAwait(false);
                if (!approved)
                {
                    return;
                }
            }

            await ExecuteWithRetriesAsync(run, definition, state).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            state.Status = WorkflowTaskStatus.Cancelled;
            await PersistAndRecordAsync(run, state, WorkflowEventType.WorkflowCancelled, "Cancelled.").ConfigureAwait(false);
        }
        finally
        {
            run.Semaphore.Release();
        }
    }

    private async Task<bool> RequestApprovalAsync(WorkflowRunState run, WorkflowTaskDefinition definition, TaskState state)
    {
        state.Status = WorkflowTaskStatus.AwaitingApproval;
        await PersistAndRecordAsync(
            run, state, WorkflowEventType.ApprovalRequested,
            $"Task '{definition.Id}' requires approval (risk={definition.RiskLevel}).").ConfigureAwait(false);

        var request = new Abstractions.Approvals.ApprovalRequest(
            run.Workflow.Id, definition.Id, definition.RiskLevel,
            $"Risk level {definition.RiskLevel} meets or exceeds the approval threshold.");

        var decision = await run.ApprovalService.RequestApprovalAsync(request, run.Token).ConfigureAwait(false);

        if (!decision.IsApproved)
        {
            state.Status = WorkflowTaskStatus.Skipped;
            run.Decisions.Add($"Task '{definition.Id}' rejected by {decision.DecidedBy}: {decision.Reason}");
            await PersistAndRecordAsync(run, state, WorkflowEventType.ApprovalRejected, $"Rejected by {decision.DecidedBy}: {decision.Reason}").ConfigureAwait(false);
            return false;
        }

        run.Decisions.Add($"Task '{definition.Id}' approved by {decision.DecidedBy}.");
        await PersistAndRecordAsync(run, state, WorkflowEventType.ApprovalGranted, $"Approved by {decision.DecidedBy}.").ConfigureAwait(false);
        return true;
    }

    private async Task ExecuteWithRetriesAsync(WorkflowRunState run, WorkflowTaskDefinition definition, TaskState state)
    {
        while (true)
        {
            run.Token.ThrowIfCancellationRequested();

            state.Attempt++;
            state.Status = WorkflowTaskStatus.Running;
            state.StartedAtUtc = DateTimeOffset.UtcNow;
            await PersistAndRecordAsync(run, state, WorkflowEventType.TaskStarted, $"Attempt {state.Attempt} started.").ConfigureAwait(false);

            var upstreamArtifacts = await CollectUpstreamArtifactsAsync(run, definition).ConfigureAwait(false);

            using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            taskCts.CancelAfter(definition.Timeout ?? run.Options.DefaultTaskTimeout);

            TaskExecutionResponse response;
            try
            {
                response = await ExecuteSingleAttemptAsync(run, definition, upstreamArtifacts, state.Attempt, taskCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
            {
                throw; // overall workflow cancellation/budget — let the caller mark this Cancelled
            }
            catch (OperationCanceledException)
            {
                // Only the per-task timeout fired, not the overall run — treat as a transient failure.
                response = new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), $"Task '{definition.Id}' timed out.", FailureClassification.Transient);
            }
            catch (Exception ex)
            {
                response = new TaskExecutionResponse(false, null, Array.Empty<ProposedArtifact>(), ex.Message, FailureClassification.Transient);
            }

            if (response.Success)
            {
                await CompleteTaskAsync(run, definition, state, response).ConfigureAwait(false);
                return;
            }

            state.LastError = response.ErrorMessage;
            state.LastFailureClassification = response.Classification == FailureClassification.None
                ? FailureClassification.Permanent
                : response.Classification;

            var canRetry = state.LastFailureClassification == FailureClassification.Transient && state.Attempt < definition.MaxRetries;
            if (canRetry)
            {
                run.Decisions.Add($"Task '{definition.Id}' attempt {state.Attempt} failed transiently and will be retried: {response.ErrorMessage}");
                await PersistAndRecordAsync(run, state, WorkflowEventType.TaskRetried, $"Attempt {state.Attempt} failed ({response.ErrorMessage}); retrying.").ConfigureAwait(false);
                await Task.Delay(run.Options.RetryDelay, run.Token).ConfigureAwait(false);
                continue;
            }

            state.Status = WorkflowTaskStatus.Failed;
            state.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.Decisions.Add($"Task '{definition.Id}' failed permanently after {state.Attempt} attempt(s): {response.ErrorMessage}");
            run.Risks.Add($"Task '{definition.Id}' failed and may require human follow-up: {response.ErrorMessage}");
            await PersistAndRecordAsync(run, state, WorkflowEventType.TaskFailed, $"Failed permanently after {state.Attempt} attempt(s): {response.ErrorMessage}").ConfigureAwait(false);
            return;
        }
    }

    private async Task<TaskExecutionResponse> ExecuteSingleAttemptAsync(
        WorkflowRunState run,
        WorkflowTaskDefinition definition,
        IReadOnlyList<Artifact> upstreamArtifacts,
        int attempt,
        CancellationToken token)
    {
        var request = new TaskExecutionRequest(definition, upstreamArtifacts, attempt);
        var response = await run.ModelProvider.ExecuteTaskAsync(request, token).ConfigureAwait(false);

        if (response.RequestedTool is not { } invocation)
        {
            return response;
        }

        // The engine — not the model — is the final arbiter of whether a tool call is allowed.
        if (!run.ToolPolicy.IsAllowed(invocation, out var denialReason))
        {
            run.Decisions.Add($"Blocked tool '{invocation.ToolName}' requested by task '{definition.Id}': {denialReason}");
            return response with { Success = false, ErrorMessage = denialReason, Classification = FailureClassification.Permanent };
        }

        var toolResult = await run.ToolRunner.RunAsync(invocation, token).ConfigureAwait(false);
        if (!toolResult.Success)
        {
            // Carry the tool's real output into the failure detail — this is the genuine evidence
            // of *why* it failed (e.g. which tests failed and how), not merely that it failed.
            var baseError = toolResult.ErrorMessage ?? "Tool execution failed.";
            var failureDetail = string.IsNullOrEmpty(toolResult.Output) ? baseError : $"{baseError}\n{toolResult.Output}";
            return response with { Success = false, ErrorMessage = failureDetail, Classification = FailureClassification.Transient };
        }

        run.Decisions.Add($"Task '{definition.Id}' ran tool '{invocation.ToolName}' successfully.");

        // Persist the tool's real output as evidence (e.g. actual dotnet build/test stdout+stderr)
        // rather than only logging that it "succeeded" — this is the durable, inspectable proof
        // that a real command ran, not a simulated or narrated result.
        if (!string.IsNullOrEmpty(toolResult.Output))
        {
            var toolOutputArtifact = new ProposedArtifact(
                $"{definition.Id}.{invocation.ToolName}.output.txt", "text/plain", toolResult.Output);
            return response with { Artifacts = response.Artifacts.Append(toolOutputArtifact).ToList() };
        }

        return response;
    }

    private async Task CompleteTaskAsync(WorkflowRunState run, WorkflowTaskDefinition definition, TaskState state, TaskExecutionResponse response)
    {
        foreach (var proposed in response.Artifacts)
        {
            var artifact = new Artifact
            {
                Id = Guid.NewGuid().ToString("n"),
                WorkflowId = run.Workflow.Id,
                TaskId = definition.Id,
                Name = proposed.Name,
                ContentType = proposed.ContentType,
                Content = proposed.Content,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            await run.ArtifactStore.SaveAsync(artifact, run.Token).ConfigureAwait(false);
            state.ArtifactIds.Add(artifact.Id);
        }

        state.Status = WorkflowTaskStatus.Completed;
        state.CompletedAtUtc = DateTimeOffset.UtcNow;
        state.LastFailureClassification = FailureClassification.None;
        await PersistAndRecordAsync(run, state, WorkflowEventType.TaskCompleted, $"Completed on attempt {state.Attempt}.").ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Artifact>> CollectUpstreamArtifactsAsync(WorkflowRunState run, WorkflowTaskDefinition definition)
    {
        var artifacts = new List<Artifact>();
        foreach (var dependencyId in definition.DependsOn)
        {
            artifacts.AddRange(await run.ArtifactStore.GetByTaskAsync(run.Workflow.Id, dependencyId, CancellationToken.None).ConfigureAwait(false));
        }

        return artifacts;
    }

    private async Task PropagateBlockedCascadesAsync(WorkflowRunState run)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var state in run.States.Values)
            {
                if (state.Status != WorkflowTaskStatus.Pending)
                {
                    continue;
                }

                var definition = run.Graph.GetTask(state.TaskId);
                var blockingDependency = definition.DependsOn.FirstOrDefault(dep => NonCompletedTerminalStatuses.Contains(run.States[dep].Status));
                if (blockingDependency is null)
                {
                    continue;
                }

                state.Status = WorkflowTaskStatus.Blocked;
                run.Decisions.Add($"Task '{state.TaskId}' blocked because dependency '{blockingDependency}' did not complete successfully.");
                await PersistAndRecordAsync(run, state, WorkflowEventType.TaskBlocked, $"Blocked by dependency '{blockingDependency}'.").ConfigureAwait(false);
                changed = true;
            }
        } while (changed);
    }

    private static bool IsTerminal(WorkflowTaskStatus status) =>
        status is WorkflowTaskStatus.Completed or WorkflowTaskStatus.Failed or WorkflowTaskStatus.Blocked
            or WorkflowTaskStatus.Skipped or WorkflowTaskStatus.Cancelled;

    private static WorkflowStatus DetermineFinalStatus(WorkflowRunState run)
    {
        var statuses = run.States.Values.Select(s => s.Status).ToList();

        if (statuses.Any(s => s == WorkflowTaskStatus.Cancelled))
        {
            return WorkflowStatus.Cancelled;
        }

        if (statuses.Any(s => s is WorkflowTaskStatus.Failed or WorkflowTaskStatus.Blocked or WorkflowTaskStatus.Skipped))
        {
            return WorkflowStatus.Failed;
        }

        return WorkflowStatus.Completed;
    }

    private WorkflowExecutionReport BuildReport(WorkflowRunState run, WorkflowStatus finalStatus, DateTimeOffset startedAtUtc)
    {
        var taskReports = run.Graph.Tasks
            .Select(t => run.States[t.Id])
            .Select(s => new TaskExecutionReport(s.TaskId, s.Status, s.Attempt, s.StartedAtUtc, s.CompletedAtUtc, s.LastError, s.ArtifactIds.ToList()))
            .ToList();

        var assumptions = new[]
        {
            "Task execution was performed by the configured IModelProvider implementation (a deterministic fake unless a live provider was supplied).",
            "Tool invocations, if any, were restricted to the configured allow-list policy."
        };

        var limitations = new List<string>
        {
            "This run did not involve a live AI model provider unless one was explicitly configured."
        };

        if (run.Token.IsCancellationRequested)
        {
            limitations.Add("Execution was cancelled or exceeded its budget before all tasks reached a terminal state.");
        }

        return new WorkflowExecutionReport(
            run.Workflow.Id,
            finalStatus,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            taskReports,
            run.Decisions.ToList(),
            run.Risks.ToList(),
            assumptions,
            limitations);
    }

    private Task PersistAndRecordAsync(WorkflowRunState run, TaskState state, WorkflowEventType eventType, string details)
    {
        return PersistAndRecordInternalAsync(run, state, eventType, details);
    }

    private async Task PersistAndRecordInternalAsync(WorkflowRunState run, TaskState state, WorkflowEventType eventType, string details)
    {
        await run.StateStore.SaveTaskStateAsync(run.Workflow.Id, state, CancellationToken.None).ConfigureAwait(false);
        await RecordEventAsync(run, state.TaskId, eventType, details).ConfigureAwait(false);
    }

    private async Task RecordEventAsync(WorkflowRunState run, string? taskId, WorkflowEventType eventType, string details)
    {
        var executionEvent = new WorkflowExecutionEvent(run.Workflow.Id, taskId, eventType, details, DateTimeOffset.UtcNow);
        await run.StateStore.AppendEventAsync(executionEvent, CancellationToken.None).ConfigureAwait(false);
    }
}
