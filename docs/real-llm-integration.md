# Real LLM integration

This document describes how the system is designed to accept a real, live LLM-backed
`IModelProvider` in place of the deterministic fake used throughout this assignment, and exactly
what would need to change — and, just as importantly, what would **not** need to change — to do
so. No real LLM API, API key, or paid AI service was used anywhere in this repository; everything
described as "executed" or "verified" elsewhere in these docs was produced by the deterministic
fake provider described below, driving the same real orchestration, validation, tool-execution,
and reporting code that a live provider would also drive unmodified.

## The seam: `IModelProvider`

Everything the orchestrator asks of "the model" goes through exactly two methods, defined in
`EngineeringOrchestrator.Abstractions`:

```csharp
public interface IModelProvider
{
    Task<TaskDecompositionResponse> DecomposeAsync(TaskDecompositionRequest request, CancellationToken cancellationToken);
    Task<TaskExecutionResponse> ExecuteTaskAsync(TaskExecutionRequest request, CancellationToken cancellationToken);
}
```

`WorkflowEngine` (the real scheduler — DAG validation, bounded parallelism, retries, approval
gates, budgets, cancellation, persistence, reporting) only ever talks to this interface. It has no
knowledge of whether the implementation behind it is a hardcoded fake, a scripted test double, or
a live model — and that is the point: swapping the implementation requires **zero changes** to
`WorkflowEngine`, `WorkflowGraph`, the tool-execution pipeline, the approval pipeline, or the
reporting pipeline.

## What this assignment actually ships

`ScenarioFakeModelProvider` (`src/EngineeringOrchestrator/Scenarios/ScenarioFakeModelProvider*.cs`)
is a deterministic, scenario-id-keyed implementation used for all three required scenarios:

- `DecomposeAsync` returns a hardcoded `WorkflowTaskDefinition[]` selected by `scenarioId` (and, for
  the brownfield re-plan path, by `request.Context == "replan"`). It does not parse or reason about
  `request.Requirement` at all — the task graph is fixed ahead of time per scenario.
- `ExecuteTaskAsync` returns a hardcoded `TaskExecutionResponse` selected by `request.Task.Id`,
  including (for implementation tasks) an embedded `ToolInvocation` whose arguments are hardcoded
  source file contents (see `GeneratedFileSets.*.cs`).
- Both methods support test-only overrides (`OverrideTaskResponse`, `OverrideDecomposition`) used
  to inject malformed plans, dangling dependencies, and scripted failures in automated tests
  without touching the scenario defaults.

There is also an older, more general `DeterministicFakeModelProvider`
(`src/EngineeringOrchestrator/ModelProviders/DeterministicFakeModelProvider.cs`) used throughout
the orchestrator's own unit test suite, which lets a test script an arbitrary sequence of
responses per task id and decomposition call — useful for testing engine behavior (retries,
timeouts, approval gates) in isolation from any scenario.

Neither implementation makes a network call, reads an API key, or depends on any external service.
Both are fully synchronous in effect (the `Task<T>` return is only to satisfy the interface) and
fully reproducible — the same input always produces the same output, which is what makes the
scenario runs in [scenario-results.md](scenario-results.md) reproducible on any machine with the
.NET 8 SDK installed, with no internet access and no credentials.

## What a real `IModelProvider` would need to do differently

A production implementation (e.g. `OpenAiModelProvider`, `AzureOpenAiModelProvider`,
`AnthropicModelProvider`) would need to:

1. **Translate `TaskDecompositionRequest.Requirement` into a decomposition prompt** and parse the
   model's response into `WorkflowTaskDefinition[]` with a **strict, validated schema** — ids,
   names, `DependsOn` arrays referencing only other ids in the same response, `RiskLevel`,
   `MaxRetries`, `AcceptanceCriteria`. Since `WorkflowGraph.Validate` already rejects cycles,
   duplicate ids, and dangling dependencies before any task runs, a real provider does not need to
   re-implement that validation — it only needs to produce well-formed JSON/structured output
   (e.g. via function calling / structured output / JSON mode) and handle the case where the model
   returns something that fails validation by treating it as a `TaskDecompositionResponse` the
   caller can inspect and retry, or by returning a safe single-task "manual review required"
   fallback (the same pattern `DeterministicFakeModelProvider` already uses).
2. **Translate `TaskExecutionRequest` (the task definition plus upstream artifacts) into an
   execution prompt**, and parse the model's response into a `TaskExecutionResponse`. Crucially,
   if the model wants to run a tool, it must express that as **at most one** `ToolInvocation` with
   a `ToolName` drawn from a small, known vocabulary (`write-files`, `list-directory`, `read-file`,
   `dotnet-build`, `dotnet-test`, or whatever vocabulary a given `IToolRunner` exposes) — never a
   free-form shell string. This is enforced structurally today by `ToolInvocation` having a
   `ToolName` + `IReadOnlyDictionary<string,string> Arguments` shape, not a command string.
3. **Never bypass the engine's own safety boundary.** A live provider's output is still just data:
   `WorkflowEngine.ExecuteSingleAttemptAsync` checks every `ToolInvocation` against
   `IToolExecutionPolicy` (today, `AllowListToolExecutionPolicy`, deny-by-default) **before**
   calling `IToolRunner`, and every task at or above the configured risk threshold still goes
   through `IApprovalService` regardless of what the model claims. A real provider gains no new
   authority a fake one didn't already have — it only changes how plans and responses are
   produced, not what is allowed to execute.
4. **Handle non-determinism responsibly.** Real model output can vary between calls. Any retry of a
   `Transient`-classified failure will invoke the model again, potentially with a different
   result — this is already how the engine's retry loop works (it calls `ExecuteTaskAsync` again on
   each attempt), so no engine change is required, but a production provider should log/attribute
   each attempt's raw model output for auditability (today, `ScenarioFakeModelProvider` logs every
   call via its own lightweight `InvocationLog` for the same reason, scoped to tests).
5. **Supply real credentials out of process configuration**, never hardcoded — e.g. an API key read
   from `IConfiguration`/environment variables/a secret manager, following the exact pattern
   already used for `Jwt:SigningKey` in the URL shortener (fail fast at startup if missing, never a
   built-in default).
6. **Respect cancellation.** `ExecuteTaskAsync`/`DecomposeAsync` already receive a
   `CancellationToken` derived from the per-task timeout and the overall execution budget,
   `WorkflowEngineOptions.DefaultTaskTimeout` / `MaxTotalDuration`); a real HTTP-based provider must
   propagate that token into its `HttpClient` call so a slow or hung model call is treated as a
   timeout (already classified `Transient` by the engine), not left to run unbounded.

## What would not change

- `WorkflowEngine` (scheduling, retries, approval gates, budgets, cancellation, resumption,
  re-planning, reporting).
- `WorkflowGraph` (cycle detection, dependency validation, topological layering).
- `IToolRunner`/`AllowListToolExecutionPolicy`/`ConstrainedWorkspaceToolRunner` (the vetted,
  parameterized tool surface and its deny-by-default policy).
- `IArtifactStore`/`IWorkflowStateStore` (persistence) and their file-backed implementations used
  by the scenario runner.
- `ScenarioRunner` (manifest loading, workspace/fixture preparation, DI composition, outcome
  computation, report writing) — it is constructed against `IModelProvider`, not against
  `ScenarioFakeModelProvider` specifically; swapping in a real provider is a one-line change in
  `ScenarioRunner.RunAsync` (and, ideally, a `ModelProvider` selector read from the manifest or an
  environment variable such as the assignment's suggested `ModelProvider=Fake`/`ModelProvider=Live`
  switch), not a rewrite.

## Honest summary

Nothing in this repository calls a real LLM. The deterministic fake providers exist specifically
so the **real** orchestration, validation, execution, and reporting pipeline could be exercised
end to end, with genuine `dotnet build`/`dotnet test` subprocess calls and genuine pass/fail
results, without any external dependency, network access, or paid service. The architecture's only
job in this area was to make sure that boundary (`IModelProvider`) is the *only* place a live
integration would need to land — which the design and the scenario runs in
[scenario-results.md](scenario-results.md) demonstrate.
