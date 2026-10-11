# Decisions log

Lightweight ADR-style log. Each entry records the decision, why it was made, and what was
considered instead. Entries are append-only; superseding decisions reference the ones they
replace rather than deleting history.

## ADR-001: Target .NET 8 (LTS), not the .NET 10 SDK also present on the machine

**Context:** Environment inspection found two installed SDKs: `8.0.425` and `10.0.401`.
**Decision:** All project files target `net8.0`; `global.json` pins the SDK to `8.0.425` with
`rollForward: latestFeature`.
**Rationale:** .NET 8 is an LTS release with mature EF Core 8 / ASP.NET Core 8 tooling and
Swashbuckle compatibility. Pinning via `global.json` avoids ambiguity if the build machine's
default SDK resolution would otherwise pick 10.x, which could silently change solution file
format (see ADR-002) or language defaults.
**Alternatives considered:** Targeting net10.0 — rejected for this assignment because it is a
newer, non-LTS SDK and increases the chance of tooling/package incompatibilities without a clear
benefit for a backend prototype.

## ADR-002: Use classic `.sln` format, not `.slnx`

**Context:** `dotnet new sln` on the .NET 10 SDK defaults to the new XML-based `.slnx` format.
The assignment explicitly asks for `AgenticUrlShortener.sln`.
**Decision:** Generated the solution with `dotnet new sln --format sln` to produce a classic
`.sln` file.
**Rationale:** Explicit instruction; `.sln` has universal tool support (VS, VS Code, CI) today,
whereas `.slnx` support is still rolling out.

## ADR-003: Git was not available in this environment

**Context:** `git --version` and `Get-Command git` both failed in the Phase 1 terminal session —
no Git executable is on `PATH` on this machine.
**Decision:** Proceeded without `git init`. Created `.gitignore` anyway so the repository is
ready to be initialized the moment Git is installed, and recorded this as an environmental
blocker rather than silently skipping the requirement.
**Follow-up:** Install Git for Windows (or re-run in an environment that has it), then run
`git init && git add -A && git commit -m "Phase 1: solution skeleton"` from the repo root.

## ADR-004: Modular monolith with orchestrator as an independent subsystem

**Context:** The assignment requires both a production-style URL shortener and an agentic
engineering orchestrator, with the explicit constraint that the orchestrator must not be on the
normal redirect path.
**Decision:** Nine projects in one solution, split into two independent dependency graphs
(`UrlShortener.*` and `EngineeringOrchestrator*`) that do not reference each other. Shared
solution/repo, no shared runtime coupling.
**Rationale:** Satisfies the "modular monolith, not microservices" instruction while structurally
guaranteeing (via the absence of a project reference) that the orchestrator cannot end up in the
redirect hot path by accident.

## ADR-005: Clean/onion-style layering for the URL Shortener

**Context:** Requirement to keep domain logic independent of ASP.NET Core and EF Core.
**Decision:** `UrlShortener.Domain` has no project or package references. `Application` depends
only on `Domain`. `Infrastructure` depends on `Domain` + `Application` and will implement
`Application`'s interfaces. `Api` is the composition root referencing all three.
**Rationale:** Standard, well-understood pattern that makes unit testing domain rules trivial
(no DB, no HTTP) and keeps persistence swappable.

## ADR-006: EF Core 8 + SQL Server packages added to `Infrastructure` only

**Context:** "Add only the NuGet packages that are required for the chosen implementation."
**Decision:** Added `Microsoft.EntityFrameworkCore.SqlServer` 8.0.11 and
`Microsoft.EntityFrameworkCore.Design` 8.0.11 to `UrlShortener.Infrastructure` only.
`Microsoft.AspNetCore.Mvc.Testing` 8.0.11 was added to `UrlShortener.IntegrationTests` since that
project's stated purpose (API + database integration tests) requires an in-process test host.
`Swashbuckle.AspNetCore` 6.6.2 ships by default in the ASP.NET Core 8 Web API template and was
kept for Swagger/OpenAPI as required by the assignment.
**Rationale:** No packages were added to `Domain`, `Application`, `EngineeringOrchestrator`, or
`EngineeringOrchestrator.Abstractions` in Phase 1 because no implementation code requiring them
exists yet; adding them speculatively would violate the "only what's required" instruction.
**Follow-up:** Revisit when implementing persistence (Phase 4) — may add
`Microsoft.Extensions.Caching.Memory`, rate-limiting is built into ASP.NET Core 8 itself (no
extra package needed), and orchestrator persistence package choices will be recorded here when
made.

## ADR-007: No placeholder business classes in Phase 1

**Context:** Instruction: "Do not create large numbers of placeholder classes. Keep the project
skeleton minimal and compilable."
**Decision:** Removed all template-generated boilerplate (`WeatherForecastController`,
`Class1.cs`, `UnitTest1.cs`) and did not replace them with speculative domain/orchestrator
classes or empty folders. Folder structure for source code will be created alongside the first
real files that belong in them (Phase 4 onward), not pre-emptively.
**Rationale:** Avoids churn and misleading structure; keeps `dotnet build`/`dotnet test` green
with zero dead code.

## ADR-008: Database-enforced short-code uniqueness via "try-and-detect" rather than a pre-check

**Context:** Short codes must be unique with the database as the source of truth, not just an
in-process check (which is racy under concurrent requests).
**Decision:** `EfShortLinkRepository.TryAddAsync` always attempts the insert directly and relies
on a unique index on `ShortLinks.Code`; it catches `DbUpdateException` and inspects the inner
exception to distinguish a real unique-constraint violation (SQL Server error 2601/2627, or a
`Microsoft.Data.Sqlite.SqliteException` with a "UNIQUE constraint failed" message) from other
failures, which are rethrown. `ShortLinkService.CreateAsync` retries with a freshly generated
code up to `ShortCodeOptions.MaxGenerationAttempts` times, then throws
`ShortCodeGenerationFailedException` (mapped to HTTP 503).
**Rationale:** Avoids a check-then-act race condition between "does code X exist?" and
"insert code X" under concurrent requests; the database unique index is the only reliable
arbiter. Detecting the SQLite exception by type name (string) rather than a hard package
reference keeps `UrlShortener.Infrastructure` from needing to reference the SQLite provider,
which is only used by tests.

## ADR-009: EF Core SQLite (in-memory, shared connection) for integration tests instead of a live SQL Server

**Context:** No SQL Server instance is available in this environment, and tests must not depend
on one being present in CI either.
**Decision:** `UrlShortener.IntegrationTests` references `Microsoft.EntityFrameworkCore.Sqlite`
and a custom `WebApplicationFactory` (`TestWebApplicationFactoryBase`) swaps the
`DbContextOptions<UrlShortenerDbContext>` registration to use a single open, shared
`SqliteConnection` with `DataSource=:memory:`, then calls `Database.EnsureCreated()` to build the
schema from the current EF model (not from the SQL-Server-targeted migration, which contains
provider-specific column type strings that SQLite cannot interpret the same way).
**Rationale:** SQLite is a real relational engine that enforces unique indexes, foreign keys, and
transactional semantics, unlike the EF Core InMemory provider — this lets
`ShortCodeUniquenessTests` prove the database (not just application code) rejects duplicate
codes. The migration itself is still SQL-Server-specific and is the artifact that would be
applied to a real production database via `dotnet ef database update`.
**Limitation:** Discovered during this work that EF Core's default `ValueGeneratedOnAdd`
convention for `Guid` primary keys, combined with SQLite's batched `SaveChanges` when an
UPDATE and an INSERT occur in the same call (e.g. incrementing `ShortLink.ClickCount` while
adding a new `ClickEvent`), produced a false `DbUpdateConcurrencyException`. Fixed by explicitly
configuring `ValueGeneratedNever()` on both `ShortLink.Id` and `ClickEvent.Id`, since the domain
layer always assigns these via `Guid.NewGuid()` and never needs the database to generate them.
This is recorded here because it is a correctness fix to the persistence model, not merely a
test workaround — the same configuration applies against SQL Server too.

## ADR-010: Rate limiting resolves options per-request, not once at startup

**Context:** ASP.NET Core's built-in rate limiter (`System.Threading.RateLimiting`, no extra
NuGet package required) is configured in `Program.cs` via `AddRateLimiter`.
**Decision:** The `GlobalLimiter` and the `"creation"` named policy both resolve
`IOptions<RateLimitOptions>` from `context.RequestServices` inside their partition-factory
lambdas, rather than capturing a `RateLimitOptions` snapshot in a local variable at startup.
**Rationale:** A local variable captured via `builder.Configuration.Get<RateLimitOptions>()`
before `builder.Build()` can observe configuration before a test host's
`ConfigureAppConfiguration` override is fully applied, silently locking in default values. The
per-request resolution pattern is also the more correct general practice for configuration that
should support reloads (`IOptionsMonitor`-like freshness) without restarting the process.

## ADR-011: Redirect endpoint lives at the application root (`GET /{code}`)

**Context:** True URL shorteners redirect from the root of the host
(`https://short.ly/abc123`), but this must not collide with `api/*` or `swagger` routes.
**Decision:** `RedirectController` maps `GET /{code:regex(^[[0-9A-Za-z]]+$)}` at the root. ASP.NET
Core's routing precedence naturally prefers the more specific, multi-segment literal routes
(`api/links`, `swagger`) over a single-segment parameterized route, so no real ambiguity exists
for the routes this application defines.
**Gotcha:** ASP.NET Core route templates treat `[` and `]` as token-replacement syntax; a
character-class regex constraint must escape them as `[[` and `]]` (e.g.
`{code:regex(^[[0-9A-Za-z]]+$)}`), otherwise route registration throws at startup.

## ADR-012: Build/test stabilization pass — real SQL Server used where it mattered

**Context:** A full validation pass (restore, build, test, runtime check) found this development
machine actually has a working SQL Server LocalDB instance (`MSSQLLocalDB`) and a running
SQL Server Express service, contrary to the Phase 1 assumption that no database engine was
available. `dotnet --info` confirms SDK `8.0.425`; `dotnet restore` and `dotnet build --no-restore`
both succeeded with 0 warnings/0 errors before any changes in this pass.
**Findings and fixes:**
1. The EF Core `InitialCreate` migration was applied for the first time against a real SQL Server
   LocalDB database (`dotnet ef database update`) and verified to create `ShortLinks`,
   `ClickEvents`, the unique index on `Code`, and the cascading foreign key correctly.
2. The API was started (`dotnet run`) against that real database and exercised with live HTTP
   requests: create (201), invalid URL (400), metadata (200), redirect (302 with correct
   `Location`), click-count analytics incrementing correctly across repeated redirects, unknown
   code (404), and deactivate-then-redirect (410) — all verified by inspecting actual responses,
   not assumed.
3. **Gap found:** the SQLite-backed integration test factory shares a single physical
   `SqliteConnection` across all requests, which cannot safely demonstrate true concurrent writes
   (SQLite connections aren't safe for concurrent command execution the way pooled SQL Server
   connections are). Added `ConcurrentShortCodeCreationTests`, which connects directly to the real
   LocalDB instance and fires 10 parallel `TryAddAsync` calls for the same code from independent
   `DbContext`/connection instances — exactly 1 succeeds, proving the database (not application
   locking) enforces uniqueness under real concurrency. It self-skips (returns without asserting)
   if no SQL Server/LocalDB is reachable, since this is the one test in the suite that depends on
   an external service.
4. **Gap found:** no integration test exercised true time-based expiry (only deactivation). Domain
   rules correctly forbid creating an already-expired link, so `ExpiryTests` creates a link with a
   valid near-future expiry and then backdates `ExpiresAtUtc` directly via the `DbContext` (the
   same state the link would reach on its own) to verify the redirect returns 410 and does not
   record a click, and that metadata reports `IsExpired=true` while remaining `IsActive=true`.
5. **Significant gap found:** the orchestrator had no resume/recovery capability — task state was
   persisted but never read back, so re-running `ExecuteAsync` for the same workflow id always
   restarted every task from scratch, contradicting the assignment's explicit "persisted workflow
   and task states" and "recovery" requirements. Added `IWorkflowEngine.ResumeAsync`, which loads
   prior `TaskState`s from `IWorkflowStateStore`, keeps already-`Completed` tasks completed, and
   resets everything else to `Pending` while preserving the prior attempt count (so a resume
   cannot be used to bypass `MaxRetries`). Covered by three new tests: resume skips completed work
   and retries only the previously-failed task, resume with no prior state behaves like a fresh
   `ExecuteAsync`, and resume preserves the attempt counter across the retry budget.
6. **Minor gaps found and closed:** no test exercised a per-task `Timeout` distinct from the
   overall execution budget (added `WorkflowEngineTimeoutTests`, using a fake model provider whose
   first attempt is deliberately slower than the configured timeout, confirming timeout is treated
   as a transient failure and retried, and that it fails permanently once retries are exhausted);
   no domain test exercised multiple accumulated clicks (added
   `RecordClick_CalledMultipleTimes_AccumulatesCountAndEvents`).
**Result:** 63 automated tests across all three test projects, all passing, 0 skipped, verified
stable across repeated runs. No test was weakened or removed to make it pass; every fix addressed
a genuine root cause (a real EF/SQLite interaction bug was NOT reintroduced — see ADR-009 — and
the gaps above were previously untested behavior, not regressions).

## ADR-013: JWT Bearer authentication and resource-level authorization

**Context:** The URL shortener had no authentication or authorization — any caller could create,
read, or deactivate any link.
**Decision:**
- Added `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.11 and configured signature, issuer,
  audience, and lifetime validation (`TokenValidationParameters` with `ValidateIssuer`,
  `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey` all `true`). `MapInboundClaims
  = false` so the raw `sub` claim is readable directly (not remapped to the legacy
  `ClaimTypes.NameIdentifier` URI), per current Microsoft guidance.
- `Jwt:Issuer`/`Jwt:Audience`/`Jwt:SigningKey` are bound from configuration (`JwtOptions`), resolved
  via `AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtOptions>>(...)` so the handler always
  sees the live configuration (including test-host overrides) rather than a value captured once at
  startup — the same lesson learned from the rate limiter in ADR-010 applied here pre-emptively.
- The API **fails fast at startup** (not on the first request) if `Jwt:SigningKey` is missing or
  under 32 bytes, with a message pointing at `dotnet user-secrets`. No insecure fallback/default
  key exists anywhere in the codebase.
- Two named policies: `AuthenticatedUser` (`RequireAuthenticatedUser()`, applied via
  `[Authorize(Policy = "AuthenticatedUser")]` at the `LinksController` class level) and `AdminOnly`
  (`RequireRole("Admin")`, evaluated imperatively through `IAuthorizationService` so admins can
  bypass resource ownership on the *same* endpoints regular owners use, rather than needing a
  separate admin-only endpoint).
- `GET /{code}` (`RedirectController`) is explicitly `[AllowAnonymous]`.
- `ShortLink` gained a required `OwnerId` (migration `AddShortLinkOwnerId`, indexed). It is always
  set from the validated `sub` claim in the controller — `CreateShortLinkRequest` has no owner
  field at all, so there is no code path where a request body could supply one.
- **Resource-level authorization:** `GetMetadataAsync`/`DeactivateAsync` now take a `CallerContext`
  (`UserId`, `IsAdmin`) and only proceed if `IsAdmin || link.IsOwnedBy(UserId)`.

**404 vs. 403 for cross-user access — the deliberate choice:** When a non-owner, non-admin caller
requests a code that exists but belongs to someone else, the API returns **404 Not Found** — the
exact same `ShortLinkNotFoundException` as a genuinely unknown code — rather than 403 Forbidden.
This directly satisfies the explicit requirement to "avoid disclosing whether another user's link
exists": if cross-user access returned 403 while unknown codes returned 404, the status code alone
would let a caller enumerate which codes exist and belong to other users, regardless of what the
response body says. 403 is reserved for authenticated callers who lack permission for a reason
*unrelated* to a specific resource's existence — in this codebase, the only such case is a validly
authenticated token with no `sub` claim at all (nothing to authorize against), where the
controller explicitly returns `Forbid()`.
**Alternative considered:** Returning 403 for cross-user access (common in APIs where resource IDs
are opaque/unguessable) was rejected because it directly contradicts the explicit anti-disclosure
requirement for *this* assignment, even though it is a defensible pattern elsewhere.
**Rationale for a symmetric HMAC key over asymmetric/JWKS validation:** keeps the implementation
honest about scope — this prototype does not include a production-grade identity provider (per
instructions), so a shared symmetric secret between issuer and validator is the simplest correct
setup for local development and tests. Production deployments against a real IdP (Entra ID,
Auth0, etc.) should switch to `options.Authority`/JWKS-based asymmetric key resolution instead;
this is documented in the README and is **not** implemented here, since building a production
identity provider was explicitly out of scope.
**Tests:** `TestWebApplicationFactoryBase` injects a fixed, test-only issuer/audience/signing key
(same extensibility hook already used for rate limiting); `TestJwt` mints deterministic tokens
(valid, expired, wrong signature, wrong issuer, wrong audience, missing `sub`, admin role) with
zero network calls or real identity provider dependency; `TestPrincipal` builds `ClaimsPrincipal`s
for direct, in-process policy-evaluation tests. 85 tests pass solution-wide after this change (up
from 63), including 13 new tests covering the full 401/403/404/owner/admin/anonymous matrix.

## ADR-014: Scenario runner reuses the existing engine; no second orchestrator was built

**Context:** The final phase requires three executable scenarios (greenfield, brownfield,
ambiguous) driven by a deterministic fake model provider, with real DAG validation, scheduling,
tool execution, build/test, approvals, and reporting — explicitly forbidding a second/parallel
orchestration implementation.
**Decision:** Added `EngineeringOrchestrator.Cli` (console entry point) and a `Scenarios` namespace
inside the existing `EngineeringOrchestrator` project: `ScenarioManifest` (JSON config),
`ScenarioRunner` (composition root), `ScenarioFakeModelProvider` (deterministic, scenario-id-keyed
`IModelProvider`), `GeneratedFileSets` (hardcoded source content for write-files tool calls),
`ScenarioOutcome`/`ScenarioRunResult` (scenario-level result types), plus two new infrastructure
pieces: `ConstrainedWorkspaceToolRunner` (real `dotnet build`/`dotnet test` subprocess execution,
constrained to a single workspace root with path-traversal guards) and `FileArtifactStore`/
`FileWorkflowStateStore` (genuine disk persistence per run, as opposed to the in-memory stores
used by the orchestrator's own unit tests). `ScenarioRunner` constructs the **same**
`WorkflowEngine` class used everywhere else — no new scheduler, DAG validator, or execution loop
was written.
**Two existing engine capabilities were reused, unmodified, to satisfy scenario-specific
requirements that might otherwise have tempted a bespoke mechanism:**
- The **brownfield** scenario's genuine fail → fix → rerun cycle uses the engine's pre-existing
  `WorkflowEngine.ReplanFailedTasksAsync` (added in ADR-012 for resume support) followed by a
  second `ExecuteAsync` call. When `run-tests-after-expiry-change` genuinely fails (a real,
  deliberately-inverted expiry-check condition — not a staged/fabricated failure), the scenario
  runner calls the engine's existing re-plan method, which asks the model provider for a
  replacement plan and returns a brand-new `WorkflowDefinition`; `ScenarioFakeModelProvider`
  recognizes `TaskDecompositionRequest.Context == "replan"` and returns a 3-task fix plan
  (fix-the-bug, rebuild, rerun). No new "retry the whole workflow" mechanism was built.
- The **ambiguous** scenario's "safe pause pending clarification" uses the engine's pre-existing
  approval gate (`RiskLevel >= WorkflowEngineOptions.ApprovalRequiredAtOrAbove` routes a task
  through `IApprovalService` before it runs). A new `DenyAllApprovalService` (the mirror image of
  the existing `AutoApprovalService`) rejects every request, representing "no human clarification
  has been recorded." The two material-decision tasks are marked `Critical` risk; the engine
  itself marks them `Skipped`, cascades `Blocked` to their dependent, and the scenario runner
  interprets that specific pattern (only Critical-risk tasks non-`Completed`, all analysis/report
  tasks `Completed`) as `ScenarioOutcome.NeedsClarification` rather than `Failed`. No new
  "ambiguity-detection" engine feature was built; the engine's own status is still `Failed` — the
  scenario layer is a thin, additive interpretation on top, not a bypass.
**Two genuine, pre-existing bugs were found and fixed while exercising the engine this way (not
scenario-specific workarounds — both are general engine correctness fixes):**
1. `FileWorkflowStateStore.SaveTaskStateAsync` performed an unsynchronized read-modify-write on a
   single shared per-workflow `*.tasks.json` file; with `MaxDegreeOfParallelism > 1` (every
   scenario manifest uses 2), two tasks completing around the same time raced and threw
   `IOException: ... being used by another process`. Fixed by adding a `SemaphoreSlim` around the
   whole read-modify-write cycle (mirroring the existing `lock` already used for
   `AppendEventAsync`). Covered by a new regression test
   (`FileStoresTests.FileWorkflowStateStore_ConcurrentSaveTaskState_DoesNotThrowAndKeepsAllTasks`).
2. `WorkflowEngine.ExecuteSingleAttemptAsync` discarded a tool's real stdout/stderr on success
   (only logging "ran tool X successfully") and, on failure, kept only the generic
   `ToolExecutionResult.ErrorMessage` (e.g. "dotnet test exited with code 1"), losing the actual
   evidence of *what* failed. Fixed by (a) appending a `ProposedArtifact` containing the tool's
   real output on success, so it is persisted via the normal artifact pipeline, and (b) appending
   the tool's real output to the failure `ErrorMessage` on failure, so the task's `Error` field in
   the report contains the real xUnit failure output (assertion messages, stack traces), not just
   an exit code. This is what makes the brownfield scenario's genuine test failure inspectable in
   `report.initial.json` — see [scenario-results.md](scenario-results.md).
**Verification:** the full pre-existing 85-test suite still passed after both fixes (no test
asserted exact tool-related `ErrorMessage` content or artifact counts that these changes would
have broken). 28 new tests were added across `ScenarioFakeModelProviderTests`,
`ConstrainedWorkspaceToolRunnerTests`, `FileStoresTests`, and `ScenarioRunnerTests` (the last of
which runs all three scenarios for real, including genuine `dotnet build`/`dotnet test`
subprocess calls, against the real manifests checked into `scenarios/`). Solution-wide total: 113
tests, all passing.
**Alternatives considered:** Building a separate, scenario-specific execution loop (e.g. a
"replan" helper with its own retry/ambiguity logic duplicating pieces of `WorkflowEngine`) was
rejected — it would have violated the explicit "do not rebuild it from scratch or create a second
orchestrator" constraint and would have meant two code paths to keep behaviorally consistent for
retries, cancellation, and reporting.

