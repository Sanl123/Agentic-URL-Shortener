# Scenario results

This document records the results of actually executing all three required scenarios through the
real CLI, against the real `WorkflowEngine`, with the deterministic fake model provider described
in [real-llm-integration.md](real-llm-integration.md). Every number, path, and exit code below was
captured from a real run on this machine — nothing here is narrated or assumed. `runs/` is
git-ignored (it is reproducible evidence output, not source), so re-run the commands below to
regenerate it; the specific run ids quoted here will differ on a fresh run but the outcomes are
deterministic and reproducible.

**Environment:** .NET SDK 8.0.425 (pinned via `global.json`), Windows, no network access used, no
API key/credential of any kind configured or required.

## How to reproduce

```powershell
dotnet restore
dotnet build
dotnet run --project src/EngineeringOrchestrator.Cli -- scenario run scenarios/greenfield/scenario.json
dotnet run --project src/EngineeringOrchestrator.Cli -- scenario run scenarios/brownfield/scenario.json
dotnet run --project src/EngineeringOrchestrator.Cli -- scenario run scenarios/ambiguous/scenario.json
```

Each invocation prints the scenario id, run id, scenario outcome, engine status, the run/workspace
directory, and the JSON/Markdown report paths, then exits with the scenario's exit code (`0` for
`Success`/`NeedsClarification`, `1` for `Failed`).

---

## 1. Greenfield — build a URL shortener from scratch

**Manifest:** `scenarios/greenfield/scenario.json` · **Requirements:** `scenarios/greenfield/requirements.md`

**Last real run:** run id `20261011T163205Z-c6250ae6f`, exit code `0`.

| | |
|---|---|
| Scenario outcome | **Success** |
| Engine status | **Completed** |
| Run directory | `runs/greenfield/20261011T163205Z-c6250ae6f/` |
| Report | `runs/greenfield/20261011T163205Z-c6250ae6f/report.json` / `report.md` |

### Task-by-task result

| Task | Status | Attempts |
|---|---|---|
| analyze-requirements | Completed | 1 |
| inspect-workspace | Completed | 1 |
| decompose-and-validate-dependencies | Completed | 1 |
| implement-feature | Completed | 1 |
| build-solution | Completed | 1 |
| run-tests | Completed | 1 |
| produce-final-report | Completed | 1 |

### What actually happened

1. The fake provider's `DecomposeAsync` returned a fixed 7-task DAG; `WorkflowGraph.Validate`
   accepted it (no cycles, no dangling dependencies).
2. `implement-feature` issued a real `write-files` tool call that wrote a 3-project solution into
   the run's isolated workspace: `GeneratedUrlShortener.Core` (classlib: `ShortLink`, `LinkStore`),
   `GeneratedUrlShortener.Api` (minimal API: `POST /links`, `GET /{code}`), and
   `GeneratedUrlShortener.Tests` (xUnit, 3 tests covering AC1–AC4).
3. `build-solution` ran **a real `dotnet build`** against `GeneratedUrlShortener.Api.csproj` inside
   the workspace (which transitively builds `Core`), captured as the artifact
   `build-solution.dotnet-build.output.txt`.
4. `run-tests` ran **a real `dotnet test`** against `GeneratedUrlShortener.Tests.csproj`, captured
   verbatim as the artifact `run-tests.dotnet-test.output.txt`:

   ```
   Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 5 ms - GeneratedUrlShortener.Tests.dll (net8.0)
   ```

5. `produce-final-report` completed, closing out the workflow as `Completed`.

No task failed, no approval was required (all tasks are Low/Medium risk; the configured threshold
is High), and the generated solution's tests passed for real.

---

## 2. Brownfield — add link expiry to a legacy fixture (genuine fail → fix → rerun)

**Manifest:** `scenarios/brownfield/scenario.json` · **Requirements:** `scenarios/brownfield/requirements.md`
**Fixture:** `scenarios/brownfield/fixture/{LegacyUrlShortener,LegacyUrlShortener.Tests}` (copied
into an isolated run workspace before execution; the checked-in fixture is never modified).

**Last real run:** run id `20261011T163223Z-90e078a1a`, exit code `0`.

| | |
|---|---|
| Scenario outcome | **Success** |
| Final engine status | **Completed** (after one genuine re-plan cycle) |
| Run directory | `runs/brownfield/20261011T163223Z-90e078a1a/` |
| Initial attempt report | `runs/brownfield/20261011T163223Z-90e078a1a/report.initial.json` / `.md` |
| Fix-cycle report (final) | `runs/brownfield/20261011T163223Z-90e078a1a/report.json` / `.md` |

### What actually happened — attempt 1 (genuinely fails)

| Task | Status | Attempts | Error |
|---|---|---|---|
| inspect-fixture | Completed | 1 | |
| run-baseline-tests | Completed | 1 | |
| decompose-expiry-plan | Completed | 1 | |
| add-expiry-field-and-check | Completed | 1 | |
| build-after-expiry-change | Completed | 1 | |
| run-tests-after-expiry-change | **Failed** | 1 | `dotnet test exited with code 1.` |

1. `run-baseline-tests` ran the fixture's pre-existing 3 tests for real before any change —
   **3/3 passed** (captured in `run-baseline-tests.dotnet-test.output.txt`).
2. `add-expiry-field-and-check` wrote a real (and, on this first attempt, genuinely buggy) change:
   `ShortLink` gained an optional `ExpiresAtUtc`, and `LinkStore.TryGet` gained an expiry check —
   but with an **inverted condition** (`ExpiresAtUtc.Value > nowUtc` instead of `<= nowUtc`), plus
   3 new tests (`ExpiryTests.cs`) exercising expired/valid/no-expiry cases.
3. `build-after-expiry-change` built successfully (the bug is a logic error, not a compile error).
4. `run-tests-after-expiry-change` ran **a real `dotnet test`**, which **genuinely failed** — 2 of
   the 3 new tests failed for the expected, opposite reasons (captured verbatim in the task's
   `Error` field and in the initial report):

   ```
   Failed LegacyUrlShortener.Tests.ExpiryTests.TryGet_ValidNonExpiredLink_ReturnsLink [< 1 ms]
     Assert.NotNull() Failure: Value is null
   Failed LegacyUrlShortener.Tests.ExpiryTests.TryGet_ExpiredLink_ReturnsNull [4 ms]
     Assert.Null() Failure: Value is not null
     Expected: null
     Actual:   ShortLink { Code = "expired1", ... }
   ```

   This is a real xUnit failure, not fabricated: the inverted condition makes a *valid* (future
   expiry) link look expired, and an *expired* (past expiry) link look valid — exactly backwards.

### What actually happened — re-plan and fix (genuinely passes)

Because a task genuinely failed, the scenario runner invoked the engine's existing
`WorkflowEngine.ReplanFailedTasksAsync`, which called the fake provider's `DecomposeAsync` with
`Context="replan"` and received a 3-task fix plan, then ran it:

| Task | Status | Attempts |
|---|---|---|
| fix-expiry-check-bug | Completed | 1 |
| rebuild-after-fix | Completed | 1 |
| rerun-tests-after-fix | Completed | 1 |

- `fix-expiry-check-bug` wrote a corrected `LinkStore.cs` (condition flipped to `<= nowUtc`).
- `rebuild-after-fix` built successfully.
- `rerun-tests-after-fix` ran **a real `dotnet test`** a second time:

  ```
  Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 2 ms - LegacyUrlShortener.Tests.dll (net8.0)
  ```

  6/6 = the 3 original baseline tests (regression-checked, still passing) + the 3 new expiry tests
  (now genuinely passing).

### Fixture isolation, verified

After the run, `scenarios/brownfield/fixture/LegacyUrlShortener/ShortLink.cs` on disk was
re-read and diffed against its content **before** the run — byte-for-byte identical. All writes
happened only inside `runs/brownfield/<runId>/workspace/`.

---

## 3. Ambiguous — "Make redirects fast and analytics accurate." (safe pause)

**Manifest:** `scenarios/ambiguous/scenario.json` (`approvalMode: "DenyAll"`) · **Requirements:**
`scenarios/ambiguous/requirements.md` (verbatim first line: *"Make redirects fast and analytics
accurate."*).

**Last real run:** run id `20261011T163343Z-ce4f5b90a`, exit code `0`.

| | |
|---|---|
| Scenario outcome | **NeedsClarification** (not Failed) |
| Raw engine status | `Failed` (expected — see below) |
| Run directory | `runs/ambiguous/20261011T163343Z-ce4f5b90a/` |
| Report | `runs/ambiguous/20261011T163343Z-ce4f5b90a/report.json` / `.md` |

### Task-by-task result

| Task | Status | Notes |
|---|---|---|
| analyze-ambiguous-requirement | Completed | Produced `ambiguity-analysis.json` |
| inspect-current-implementation | Completed | Read-only `list-directory`, no writes |
| propose-performance-and-analytics-improvements | **Skipped** | Critical risk; rejected by `DenyAllApprovalService` |
| deploy-performance-improvements | **Blocked** | Cascaded from the rejected dependency above |
| produce-ambiguity-report | Completed | Produced `ambiguity-report.md`; depends only on task 1 |

### Why this is a success, not a failure

The engine's raw `WorkflowStatus` is `Failed` because two tasks ended in non-`Completed` terminal
states (`Skipped`/`Blocked`) — that is the engine behaving exactly as designed: both tasks are
`RiskLevel.Critical`, which meets the manifest's `approvalRequiredAtOrAbove: "High"` threshold, and
the manifest's `DenyAllApprovalService` rejects every approval request because **no human
clarification has been recorded**. `ScenarioRunner.ComputeOutcome` recognizes this specific
pattern — every non-completed task is Critical-risk and was Skipped/Blocked, while every
analysis/reporting task completed — and reports it as `ScenarioOutcome.NeedsClarification`, which
maps to **exit code 0**: a safe pause is a correct outcome, not an error.

### The captured ambiguity analysis

`ambiguity-analysis.json` (artifact produced by `analyze-ambiguous-requirement`) contains the 7
required unresolved questions, verbatim:

```json
{
  "RequirementSummary": "Make redirects fast and analytics accurate.",
  "UnresolvedQuestions": [
    "What is the target redirect latency, and how will it be measured (p50/p95/p99, at which tier)?",
    "What does \"accurate\" mean for analytics — exact counts, bounded error margin, or eventual reconciliation?",
    "Should click recording happen synchronously on the redirect path, or asynchronously out-of-band?",
    "Is eventual consistency between a redirect and its recorded analytics event acceptable, and for how long?",
    "What data-loss window (if any) is acceptable for analytics events during a failure?",
    "What is the expected traffic volume/scale this needs to support?",
    "Is changing infrastructure or the existing database schema permitted to achieve this, or must it stay within current constraints?"
  ],
  "ApprovalStatus": "NotRecorded",
  "PauseReason": "Material implementation decisions (propose/deploy) require human clarification/approval that has not been recorded; the run paused safely instead of guessing."
}
```

No file was written under `proposed-changes/` or `deployed/` in the run workspace — the two
Critical-risk tasks never reached `ExecuteTaskAsync` at all, because the engine's approval gate
rejects them before any model call or tool invocation happens for that task.

---

## Summary

| Scenario | Outcome | Exit code | Real `dotnet build`/`test` invocations | Genuine failure observed? |
|---|---|---|---|---|
| Greenfield | Success | 0 | 1 build + 1 test (3/3 passed) | No |
| Brownfield | Success | 0 | 2 builds + 2 tests (3/3 baseline, then 2/3 new tests genuinely failed, then 6/6 after a real fix) | **Yes — then genuinely fixed** |
| Ambiguous | NeedsClarification | 0 | 0 (material tasks never ran) | N/A — safe pause by design |

All three scenarios route through the same real `WorkflowEngine`, `WorkflowGraph` validation,
`ConstrainedWorkspaceToolRunner` (real `dotnet` subprocess calls, real file writes, path-traversal
guarded), `AllowListToolExecutionPolicy`, and file-backed `IArtifactStore`/`IWorkflowStateStore`.
The only scenario-specific piece is which deterministic, hardcoded plan/response
`ScenarioFakeModelProvider` returns — see [real-llm-integration.md](real-llm-integration.md) for
exactly what would change to plug in a real model instead.
