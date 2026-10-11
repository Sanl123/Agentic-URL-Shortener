# Project Guidelines — AgenticUrlShortener

This repository hosts two independent subsystems in one modular-monolith solution: a production
URL Shortener API (`UrlShortener.*`) and an Engineering Orchestrator agentic workflow engine
(`EngineeringOrchestrator*`). See [docs/architecture.md](../docs/architecture.md) for the full
design and [docs/decisions.md](../docs/decisions.md) for the running decisions log — read these
before proposing structural changes.

## Architecture

- Strict inward layering for the URL Shortener: `Domain` (no framework deps) ← `Application` ←
  `Infrastructure` ← `Api` (composition root). `EngineeringOrchestrator.Abstractions` (contracts
  only) ← `EngineeringOrchestrator` (engine) ← `EngineeringOrchestrator.Cli`.
- The two stacks **do not reference each other** — the orchestrator must never be required to
  process a normal redirect. Don't add a project reference that would couple them.
- The orchestrator never calls a real LLM. `IModelProvider` is the integration seam; see
  [docs/real-llm-integration.md](../docs/real-llm-integration.md) before touching model-provider
  code.

## Code style and conventions

- .NET 8 (pinned via `global.json`), nullable reference types and implicit usings enabled.
- Entities use private setters with factory methods / validated constructors (see
  `UrlShortener.Domain/Entities/ShortLink.cs`), not public mutable properties.
- Use dependency injection and `async`/`await` consistently; don't block on async code.
- Validate inputs and handle errors via the existing patterns (`ExceptionHandlingMiddleware`,
  domain/application exception types) rather than ad hoc try/catch at the boundary.
- Match existing naming and folder conventions in the project you're editing before introducing a
  new pattern.

## Security

- Never commit secrets (API keys, signing keys, connection strings with embedded credentials).
  Follow the existing pattern: empty placeholders in `appsettings.json`/`appsettings.example.json`,
  real values via `dotnet user-secrets` locally or environment variables/secret managers in
  deployed environments.
- Cross-user resource access returns 404, not 403, to avoid disclosing that a resource exists for
  another user — see ADR-013 before changing authorization responses.
- Tool execution in the orchestrator is deny-by-default (`IToolExecutionPolicy`); never bypass the
  allow-list or propose arbitrary shell execution from model output.

## Build and test

```powershell
dotnet restore
dotnet build
dotnet test
```

Run from the repository root to build/test everything, or pass a specific `.csproj`/`.sln` path to
scope to one project. Baseline at last verification: 113 tests passing across
`UrlShortener.UnitTests`, `UrlShortener.IntegrationTests`, and `Orchestrator.Tests` — see
[docs/scenario-results.md](../docs/scenario-results.md) for the three agentic scenario runs.

- **Never invent or assume test/build results.** Only report a pass/fail you actually observed in
  this session.
- Add or update tests for every behavior change. Unit tests use hand-rolled fakes, not a mocking
  library (see `tests/UrlShortener.UnitTests/Fakes/`).
- Preserve backward compatibility unless the requirement explicitly calls for a breaking change.
- Require explicit human approval before any destructive action (data-deleting operations, applying
  a migration to a shared/production database) or production deployment.

## Multi-agent workflow

This repository defines a six-agent engineering team under `.github/agents/`: Orchestrator,
Technical Architect, Developer, Code Reviewer, Database Administrator, and QA & Test Engineer. For
multi-step tasks (new features, bug fixes, database changes), prefer starting from the
**Orchestrator** agent. See [docs/agent-workflow.md](../docs/agent-workflow.md) for the phased
workflow and [docs/agent-handoff-contract.md](../docs/agent-handoff-contract.md) for the delegation
request/response format, ownership rules, and approval gates. Specialist-specific rules live in
each agent's own file — this document only covers rules shared by all of them.
