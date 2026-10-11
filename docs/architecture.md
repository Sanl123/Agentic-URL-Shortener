# Architecture

## Style: modular monolith

The system is a single deployable backend (the `UrlShortener.Api` host) composed of clearly
separated modules enforced by project boundaries and compiler-checked references, not network
boundaries. This gives most of the maintainability benefits of microservices (isolation,
independent testability, clear contracts) without the operational cost (distributed transactions,
network partitioning, service discovery) that would be disproportionate for this assignment.

Two logical subsystems live in the same solution:

1. **URL Shortener domain** (`UrlShortener.*`) — the product-facing feature set.
2. **Engineering Orchestrator** (`EngineeringOrchestrator*`) — the agentic workflow engine that
   is the assignment's main differentiator.

These subsystems are deliberately **not** wired together. The orchestrator is a separate set of
projects with its own abstractions, and nothing in the redirect/create/analytics request path
depends on it. This satisfies the requirement that "the engineering orchestrator ... must not be
required to process normal URL redirects." The orchestrator could later be hosted as a background
service inside the same process, a separate worker process, or a CLI — that decision is deferred
until Phase 3/4 and will be recorded in [decisions.md](decisions.md) when made.

## Layering (URL Shortener)

```
UrlShortener.Api            -> ASP.NET Core controllers, DI composition, middleware, Swagger
      |
      v
UrlShortener.Application     -> use cases (e.g. CreateShortLink, Redirect, GetMetadata),
      |                          application-level interfaces (IShortLinkRepository, IClock,
      |                          IShortCodeGenerator), DTOs/validation
      v
UrlShortener.Domain          -> ShortLink entity, value objects, invariants (expiry, activation,
                                 click accounting). No ASP.NET Core or EF Core references.
      ^
      |
UrlShortener.Infrastructure  -> EF Core DbContext, SQL Server configuration, repository
                                 implementations, migrations. Implements Application interfaces.
```

`UrlShortener.Api` references all three lower layers so it can compose dependencies at startup;
`UrlShortener.Infrastructure` depends on `Application`'s interfaces (and `Domain`) so persistence
can be swapped without touching business rules. `Domain` has zero outward dependencies, keeping
business rules testable without a database or a web host.

## Layering (Engineering Orchestrator)

```
EngineeringOrchestrator.Abstractions -> contracts only: IModelProvider, IToolRunner,
                                          IArtifactStore, IApprovalService, task/workflow DTOs,
                                          risk levels, status enums
      ^
      |
EngineeringOrchestrator              -> DAG construction + cycle detection, dependency-aware
                                          scheduler, bounded parallel execution, retry/backoff,
                                          failure classification, approval checkpoints, budgets/
                                          timeouts/cancellation, workflow/task state persistence,
                                          report generation
```

Keeping `Abstractions` dependency-free (no EF Core, no ASP.NET Core) means a deterministic fake
model provider and an in-memory artifact store can be used in tests with zero external services,
and real providers/tool runners can be substituted later via DI without touching the engine.

## Scenario execution layer (greenfield / brownfield / ambiguous)

A third logical piece sits on top of the engine and reuses it unmodified:

```
EngineeringOrchestrator.Cli      -> console entry point: `scenario run <manifest.json>`
      |
      v
EngineeringOrchestrator/Scenarios -> ScenarioRunner (composition root), ScenarioManifest,
                                      ScenarioFakeModelProvider (deterministic, scenario-id-keyed
                                      IModelProvider), GeneratedFileSets (hardcoded source content
                                      written by the "write-files" tool), ScenarioOutcome
      |
      v
EngineeringOrchestrator/Tools      -> ConstrainedWorkspaceToolRunner (real dotnet build/test
                                      subprocess calls, path-traversal-guarded file I/O)
EngineeringOrchestrator/Stores     -> FileArtifactStore, FileWorkflowStateStore (genuine disk
                                      persistence per run, instead of the in-memory stores used by
                                      the orchestrator's own unit tests)
```

`ScenarioRunner` does not add a second engine — it is a thin composition root that: loads a
`ScenarioManifest`, creates an isolated `runs/<scenarioId>/<runId>/workspace/` (optionally seeded
from a read-only fixture under `scenarios/<scenarioId>/fixture/`), wires a
`ScenarioFakeModelProvider` + `ConstrainedWorkspaceToolRunner` + `AllowListToolExecutionPolicy` +
`FileArtifactStore` + `FileWorkflowStateStore` + an approval service (`AutoApprovalService` or
`DenyAllApprovalService`, selected by the manifest) into the **same** `WorkflowEngine` used
everywhere else, calls `DecomposeAsync` → `engine.ExecuteAsync`, and — only if a task genuinely
failed — calls the engine's existing `ReplanFailedTasksAsync` followed by a second
`ExecuteAsync`. No engine code was added or changed to support any of the three required
scenarios; see [decisions.md](decisions.md) ADR-014.

`ScenarioOutcome` (`Success` / `NeedsClarification` / `Failed`) is a scenario-level interpretation
layered on top of the engine's own `WorkflowStatus`, specifically so a correctly-paused ambiguous
run (material tasks safely `Skipped`/`Blocked` behind an unmet approval gate) is reported as a
successful safe pause rather than a failure — see [scenario-results.md](scenario-results.md) for
the full, real execution results of all three scenarios.



- Create shortened URLs with a securely generated short code (database-enforced uniqueness via a
  unique index/constraint, with bounded regeneration attempts on collision).
- Redirect by short code (hot path — must stay independent of the orchestrator).
- Retrieve link metadata (owner-visible details: target URL, created/expiry dates, active flag,
  click count).
- Expiry (time-based) and manual deactivation.
- Click analytics (append-only click events; aggregate counts/timestamps).
- Cross-cutting: input validation (model binding + FluentValidation or DataAnnotations, TBD),
  rate limiting (ASP.NET Core built-in rate limiting middleware), structured logging
  (`ILogger<T>` + scopes), and a centralized exception-handling middleware that maps domain
  exceptions to problem-details responses without leaking internals.

## Planned feature set (Engineering Orchestrator)

- Requirement intake and task decomposition (via `IModelProvider`).
- Typed tasks: id, description, dependencies, acceptance criteria, expected artifacts, risk level.
- DAG validation with explicit cycle detection before any execution is scheduled.
- Dependency-aware scheduler with bounded parallelism (e.g. `SemaphoreSlim`/`Channel`-based).
- Persisted workflow/task state (reusing SQL Server via EF Core, in a schema separate from the
  URL shortener's tables, or a dedicated store — decision deferred to Phase 4).
- Artifact tracking and decision lineage (what was produced, by which task, based on which
  decision).
- Build/test/acceptance gates between task completion and downstream task start.
- Bounded retries with failure classification (transient vs. permanent) and re-planning hooks.
- Human approval checkpoints for high-impact/high-risk actions, enforced by the engine, not by
  the model's own output.
- Timeouts, cooperative cancellation, execution budgets, and safe-stop (no partial/unsafe writes
  on forced stop).
- Final report: decisions made, evidence collected, risks, assumptions, and known limitations.

## Non-negotiable safety boundary

Model-generated plans are **data**, not code. The orchestrator engine enforces:

- A policy layer that validates proposed tasks/tool calls against an allow-list before execution.
- No arbitrary shell execution; the `IToolRunner` abstraction only exposes vetted, parameterized
  operations — never raw command-line strings from model output.
- High-impact actions always require `IApprovalService` sign-off regardless of what the model
  "decided."

This boundary is enforced in application code in `EngineeringOrchestrator`, independent of which
model provider implementation is plugged in.

## Technology choices

| Concern | Choice | Rationale |
|---|---|---|
| Runtime | .NET 8 (LTS) | Long-term support, mature EF Core 8 and ASP.NET Core 8 tooling; a .NET 10 SDK is also present but 8 is pinned via `global.json` for stability during the assignment. |
| Web framework | ASP.NET Core Web API, controller-based | Explicit routing/attributes read well in interview review; mature Swagger integration via Swashbuckle. |
| Persistence | EF Core 8 + SQL Server | Required by the assignment; code-first migrations keep schema in source control. |
| API docs | Swashbuckle.AspNetCore (OpenAPI/Swagger UI) | Ships in the .NET 8 Web API template; zero extra config to get a browsable API. |
| Testing | xUnit | Standard .NET testing framework, first-class `dotnet test` and CI support. |
| Integration tests | `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) | In-process API host for realistic HTTP-level tests without a deployed server. |

Packages are added only when a concrete implementation needs them (see
[decisions.md](decisions.md) for the running log); no speculative dependencies were added in
Phase 1.
