# Agentic URL Shortener

A backend-only prototype built for the interview assignment **"Build an Agentic Software
Engineering System — URL Shortener."**

The solution is a **modular monolith** composed of two logical subsystems that share a
process and a solution file, but do not share runtime responsibilities:

1. **URL Shortener** — a conventional ASP.NET Core Web API (create / redirect / metadata /
   analytics for shortened links), backed by EF Core and SQL Server.
2. **Engineering Orchestrator** — a DAG-based agentic workflow engine that decomposes
   engineering requirements into typed, dependency-aware tasks and executes them under
   policy, approval, and budget controls. It is **not** on the request path of a normal
   redirect.

> **Status:** URL shortener vertical slice (with JWT authentication and resource-level
> authorization), the Engineering Orchestrator core engine (DAG validation/cycle detection,
> dependency-aware bounded-parallel scheduling, retries, approval gates, cancellation/budgets,
> persistence, and resume-after-interruption), and the final phase — a deterministic fake model
> provider plus a CLI-driven scenario runner that executes all three required scenarios
> (greenfield, brownfield, ambiguous) through the real engine — are all implemented and verified.
> 113 automated tests pass across all four test areas, including tests run against a real local
> SQL Server (LocalDB) instance and 3 end-to-end scenario tests that genuinely invoke `dotnet
> build`/`dotnet test` as subprocesses — see [Test results](#test-results) below. No real LLM API,
> API key, or paid AI service is used anywhere; see
> [docs/real-llm-integration.md](docs/real-llm-integration.md) for exactly what the deterministic
> fake provider stands in for, and [docs/scenario-results.md](docs/scenario-results.md) for the
> actual captured results of all three scenarios. See [docs/decisions.md](docs/decisions.md) for
> the running decisions log and [docs/architecture.md](docs/architecture.md) for the full design.

## Solution layout

```
AgenticUrlShortener.sln
global.json                      # pins the .NET SDK used to build this repo
appsettings.example.json         # template for local secrets/config (never commit real values)
.gitignore
README.md
docs/
  architecture.md
  decisions.md
  real-llm-integration.md
  scenario-results.md
scenarios/                        # the 3 required scenarios: manifest + requirements (+ fixture)
  greenfield/
  brownfield/
  ambiguous/
runs/                             # generated scenario run evidence (git-ignored, reproducible)
src/
  UrlShortener.Api/                        ASP.NET Core Web API (composition root)
  UrlShortener.Application/                Use cases, DTOs, application-level interfaces
  UrlShortener.Domain/                     Entities, value objects, domain rules (no framework deps)
  UrlShortener.Infrastructure/              EF Core DbContext, SQL Server, repository implementations
  EngineeringOrchestrator.Abstractions/     Orchestration contracts (model provider, tool runner, artifact store, approval service)
  EngineeringOrchestrator/                  DAG engine, scheduler, policies, execution runtime, scenario runner
  EngineeringOrchestrator.Cli/              `scenario run <manifest.json>` console entry point
tests/
  UrlShortener.UnitTests/                  xUnit tests for Domain/Application
  UrlShortener.IntegrationTests/           API + database integration tests (WebApplicationFactory)
  Orchestrator.Tests/                      Orchestrator/DAG engine tests + scenario runner/tool-runner/file-store tests
```

## Project references

| Project | References |
|---|---|
| `UrlShortener.Application` | `UrlShortener.Domain` |
| `UrlShortener.Infrastructure` | `UrlShortener.Domain`, `UrlShortener.Application` |
| `UrlShortener.Api` | `UrlShortener.Domain`, `UrlShortener.Application`, `UrlShortener.Infrastructure` |
| `EngineeringOrchestrator` | `EngineeringOrchestrator.Abstractions` |
| `EngineeringOrchestrator.Cli` | `EngineeringOrchestrator`, `EngineeringOrchestrator.Abstractions` |
| `UrlShortener.UnitTests` | `UrlShortener.Domain`, `UrlShortener.Application` |
| `UrlShortener.IntegrationTests` | `UrlShortener.Api`, `UrlShortener.Infrastructure` |
| `Orchestrator.Tests` | `EngineeringOrchestrator`, `EngineeringOrchestrator.Abstractions` |

`UrlShortener.Domain` and `EngineeringOrchestrator.Abstractions` have no project references —
they are the innermost layers. The orchestrator stack (`EngineeringOrchestrator*`) does not
reference, and is not referenced by, the URL shortener stack — they are independent logical
components sharing a solution and a repository, not a runtime dependency.

## Prerequisites

- .NET SDK **8.0.425** (LTS) — pinned via [global.json](global.json). A .NET 10 SDK may also be
  installed on the machine; `global.json` ensures `dotnet` commands run in this repo resolve to 8.0.x.
- SQL Server (LocalDB, a container, or a full instance) to run the API against a real database and
  to run the true-concurrency uniqueness test. Not required to build or to run the bulk of the test
  suite (integration tests default to an in-memory SQLite substitute — see [decisions.md](docs/decisions.md)).
- Git (recommended). **Not detected on this machine** — see [docs/decisions.md](docs/decisions.md).

## Scenarios (greenfield / brownfield / ambiguous)

Three required scenarios are implemented and runnable via the CLI, each driven by a deterministic
fake model provider (no real LLM, no API key, no network access) through the **same** real
`WorkflowEngine` used everywhere else in this repo:

```powershell
dotnet run --project src/EngineeringOrchestrator.Cli -- scenario run scenarios/greenfield/scenario.json
dotnet run --project src/EngineeringOrchestrator.Cli -- scenario run scenarios/brownfield/scenario.json
dotnet run --project src/EngineeringOrchestrator.Cli -- scenario run scenarios/ambiguous/scenario.json
```

Each run creates an isolated `runs/<scenarioId>/<runId>/` directory (workspace, artifacts, state,
JSON/Markdown reports) and exits `0` for `Success`/`NeedsClarification` or `1` for `Failed`. See
[docs/scenario-results.md](docs/scenario-results.md) for the full, real, captured results
(including a genuine test failure in the brownfield scenario that is then genuinely fixed and
re-verified) and [docs/real-llm-integration.md](docs/real-llm-integration.md) for exactly what
would change to plug in a real model provider instead of the deterministic fake.

## Build

```powershell
dotnet restore
dotnet build
```

## Test

```powershell
dotnet test
```

See [Test results](#test-results) below for current counts and coverage.

## Run the API

```powershell
dotnet run --project src/UrlShortener.Api
```

Swagger UI is available at `/swagger` in the Development environment. Protected endpoints
(`POST /api/links`, `GET /api/links/{code}`, `POST /api/links/{code}/deactivate`) require a
`Authorization: Bearer <jwt>` header — see [Authentication & authorization](#authentication--authorization)
for how to configure signing and mint a token locally. `GET /{code}` (the redirect) remains anonymous.

## Test results

Last executed 2026-10-11 against the .NET 8.0.425 SDK, with a real SQL Server LocalDB instance
(`MSSQLLocalDB`) available and used for the SQL-Server-specific tests and a manual API run:

| Project | Tests | Result |
|---|---|---|
| `UrlShortener.UnitTests` | 32 | All passing |
| `UrlShortener.IntegrationTests` | 24 | All passing (21 against SQLite in-memory + 3 that require/use a real SQL Server LocalDB instance) |
| `Orchestrator.Tests` | 57 | All passing (29 core engine tests + 28 scenario/tool-runner/file-store tests, including 3 end-to-end tests that genuinely run `dotnet build`/`dotnet test` subprocesses for all three required scenarios) |
| **Total** | **113** | **All passing, 0 skipped** |

Integration coverage includes: create/redirect/metadata/deactivate happy paths, invalid-URL
validation (400), unknown-code lookup (404), redirecting an inactive (410) or **expired** (410)
link, double deactivation (409), database-enforced short-code uniqueness both sequentially and
under **true concurrent writes against real SQL Server** (10 parallel inserts of the same code —
exactly 1 succeeds), rate-limit rejection (429) on the creation endpoint, and the full JWT
authentication/authorization matrix (missing/invalid/expired tokens, owner access, cross-user
denial, admin override, anonymous redirect — see below). Orchestrator coverage includes
dependency ordering, bounded parallel execution, cycle detection, retries, per-task timeouts,
approval gates, tool allow-list enforcement, cancellation/execution budgets, artifact/state
persistence, and resuming an interrupted run without re-executing completed tasks.

## Authentication & authorization

Protected endpoints require a JWT bearer token validated for **signature, issuer, audience, and
lifetime** (`src/UrlShortener.Api/Program.cs`). Two authorization policies are registered:

| Policy | Requirement | Applied to |
|---|---|---|
| `AuthenticatedUser` | Any authenticated caller | `POST /api/links`, `GET /api/links/{code}`, `POST /api/links/{code}/deactivate` (class-level `[Authorize(Policy = "AuthenticatedUser")]` on `LinksController`) |
| `AdminOnly` | `Admin` role claim | Evaluated imperatively (via `IAuthorizationService`) to let admins bypass resource ownership |

`GET /{code}` (the redirect) is explicitly `[AllowAnonymous]` and stays off the authenticated path
entirely, per the assignment's requirement that redirects remain public.

### Resource-level authorization

Every `ShortLink` records an `OwnerId` — the JWT's `sub` claim, read from validated claims only,
**never** from request-body input. `GetMetadata`/`Deactivate` resolve the caller's identity and
role server-side and enforce: owner **or** admin, else denied. A non-owner, non-admin caller
receives the **same `404 Not Found`** as a genuinely unknown code — this is a deliberate choice
so the API never discloses whether a given code belongs to another user (see ADR-013). A validly
authenticated token that is missing a `sub` claim entirely gets `403 Forbidden` (authenticated,
but no identity to authorize against).

### Local development setup (no production-grade identity provider included)

This solution does **not** ship a token issuer — minting real user tokens is the job of a proper
identity provider (Entra ID, Auth0, Keycloak, IdentityServer/Duende, etc.) in any real deployment.
For local development/testing, configure the API to validate tokens from whatever issuer you
control:

1. Set the signing key via **user secrets** (never commit it):
   ```powershell
   cd src/UrlShortener.Api
   dotnet user-secrets set "Jwt:Issuer" "<your-issuer>"
   dotnet user-secrets set "Jwt:Audience" "<your-audience>"
   dotnet user-secrets set "Jwt:SigningKey" "<a random value, at least 32 bytes>"
   ```
   Equivalently, set `Jwt__Issuer`, `Jwt__Audience`, and `Jwt__SigningKey` as environment variables
   (the double-underscore is ASP.NET Core's configuration-section separator). The API fails fast
   at startup with a clear error if `Jwt:SigningKey` is missing or shorter than 32 bytes — it will
   never fall back to an insecure built-in default.
2. Mint a token from an issuer that uses the **same** issuer/audience/signing key. For local
   testing without a real identity provider, the test project's `TestJwt` helper
   (`tests/UrlShortener.IntegrationTests/TestAuth/TestJwt.cs`) shows exactly how to construct one
   with `System.IdentityModel.Tokens.Jwt` — the claims that matter are `sub` (becomes the link
   owner) and, for admin access, a `role` claim of `Admin`.
3. Call the API with `Authorization: Bearer <token>`.

**Production guidance:** point `Jwt:Issuer`/`Jwt:Audience`/`Jwt:SigningKey` at a real identity
provider's values (for asymmetric providers like Entra ID/Auth0, swap `IssuerSigningKey` for
`AddJwtBearer(options => options.Authority = "...")`-style metadata-based key resolution instead
of a shared symmetric secret — this prototype uses a symmetric HMAC key for simplicity, which is
**only appropriate when the API itself is also the token issuer** or shares a secret through a
properly managed secret store, not a general-purpose production recommendation). Never commit a
real signing key to source control; use an environment variable or a secret manager (Azure Key
Vault, AWS Secrets Manager, etc.) in any deployed environment.

### How the automated tests authenticate

Integration tests never depend on a real identity provider or network call. `TestWebApplicationFactoryBase`
injects a fixed, test-only issuer/audience/signing key into the test host's configuration (the same
extensibility point already used for the database connection string and rate-limit thresholds), and
`TestJwt`/`TestPrincipal` mint deterministic tokens/principals signed with that same key for each
scenario (valid, expired, wrong signature, wrong issuer/audience, missing claims, admin role).

The API was also started against the real LocalDB database and exercised with live HTTP requests
(create, redirect, click-count analytics, 404, 410) — see [docs/decisions.md](docs/decisions.md)
ADR-012 for the full results.

## Known limitations

- No real LLM/AI model is integrated anywhere — `ScenarioFakeModelProvider` and
  `DeterministicFakeModelProvider` are both deterministic, hardcoded/scripted stand-ins. See
  [docs/real-llm-integration.md](docs/real-llm-integration.md) for the integration seam and what a
  real provider would need to do differently (nothing in the engine, tool execution, approval, or
  reporting pipeline would need to change).
- Click analytics is count + last-accessed timestamp only; no per-day/per-referrer aggregation.
- The scenario runner's generated greenfield solution is validated at the unit-test level only (no
  HTTP-level/WebApplicationFactory testing of the generated API) — appropriate for deterministic
  fake-provider-generated demonstration code, not a claim of production rigor equivalent to the
  hand-written `UrlShortener.Api`.
- `runs/` (scenario execution evidence) is git-ignored; re-run the scenario commands to regenerate
  it locally — see [docs/scenario-results.md](docs/scenario-results.md) for the last captured
  results and reproduction steps.

## Roadmap

See the task breakdown in the assignment brief. Work proceeded in incremental, reviewable
phases: (1) solution skeleton, (2) URL shortener vertical slice, (3) DAG engine and scheduler,
(4) execution persistence/retries/approvals, (5) build/test validation and stabilization,
(6) JWT authentication/authorization, (7) deterministic fake model provider + CLI-driven scenario
runner + all three required scenarios (greenfield/brownfield/ambiguous) executed and verified for
real — **all done**. See [docs/real-llm-integration.md](docs/real-llm-integration.md) for what
remains out of scope by design (a live LLM integration) and exactly how it would be added.
