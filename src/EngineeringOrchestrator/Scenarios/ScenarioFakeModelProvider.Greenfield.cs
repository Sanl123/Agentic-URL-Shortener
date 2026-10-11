using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Scenarios;

public sealed partial class ScenarioFakeModelProvider
{
    private static TaskDecompositionResponse GreenfieldPlan()
    {
        var tasks = new WorkflowTaskDefinition[]
        {
            new()
            {
                Id = "analyze-requirements",
                Name = "Analyze requirements and record acceptance criteria",
                RiskLevel = RiskLevel.Low,
                ExpectedArtifacts = new[] { "acceptance-criteria.md" }
            },
            new()
            {
                Id = "inspect-workspace",
                Name = "Inspect workspace and architecture",
                RiskLevel = RiskLevel.Low,
                ExpectedArtifacts = new[] { "workspace-inspection.md" }
            },
            new()
            {
                Id = "decompose-and-validate-dependencies",
                Name = "Decompose work and validate dependencies",
                RiskLevel = RiskLevel.Low,
                DependsOn = new[] { "analyze-requirements", "inspect-workspace" },
                ExpectedArtifacts = new[] { "task-plan.md" }
            },
            new()
            {
                Id = "implement-feature",
                Name = "Implement the URL shortener feature",
                RiskLevel = RiskLevel.Medium,
                DependsOn = new[] { "decompose-and-validate-dependencies" },
                MaxRetries = 2,
                ExpectedArtifacts = new[] { "implementation-summary.md" },
                AcceptanceCriteria = new[]
                {
                    "AC1: creating a link returns a generated code and the original URL",
                    "AC2: a created code resolves back to its original URL",
                    "AC3: creating with an invalid URL is rejected",
                    "AC4: looking up an unknown code reports not-found, not an exception"
                }
            },
            new()
            {
                Id = "build-solution",
                Name = "Build the generated solution",
                RiskLevel = RiskLevel.Medium,
                DependsOn = new[] { "implement-feature" },
                MaxRetries = 2
            },
            new()
            {
                Id = "run-tests",
                Name = "Run the generated solution's tests",
                RiskLevel = RiskLevel.Medium,
                DependsOn = new[] { "build-solution" },
                MaxRetries = 2
            },
            new()
            {
                Id = "produce-final-report",
                Name = "Produce the final engineering report",
                RiskLevel = RiskLevel.Low,
                DependsOn = new[] { "run-tests" },
                ExpectedArtifacts = new[] { "final-report.md" }
            }
        };

        return new TaskDecompositionResponse(tasks, "Greenfield plan: analyze, inspect, decompose, implement, build, test, report.");
    }

    private static TaskExecutionResponse GreenfieldTaskResponse(TaskExecutionRequest request) => request.Task.Id switch
    {
        "analyze-requirements" => Success(
            "Recorded 4 acceptance criteria from the greenfield requirements document.",
            new[] { new ProposedArtifact("acceptance-criteria.md", "text/markdown", GreenfieldAcceptanceCriteriaMarkdown) }),

        "inspect-workspace" => Success(
            "Workspace inspected; ready for a fresh generated solution.",
            new[] { new ProposedArtifact("workspace-inspection.md", "text/markdown", "# Workspace inspection\n\nWorkspace is empty and writable. No pre-existing generated solution to preserve.") },
            tool: ListDirectory(".")),

        "decompose-and-validate-dependencies" => Success(
            "Task graph validated: 7 tasks, linear critical path, no cycles.",
            new[] { new ProposedArtifact("task-plan.md", "text/markdown", GreenfieldTaskPlanMarkdown) }),

        "implement-feature" => Success(
            "Wrote GeneratedUrlShortener.Core, .Api, and .Tests projects.",
            new[] { new ProposedArtifact("implementation-summary.md", "text/markdown", GreenfieldImplementationSummaryMarkdown) },
            tool: WriteFiles(GeneratedFileSets.GreenfieldImplementationFilesJson())),

        "build-solution" => Success(
            "Build requested for GeneratedUrlShortener.Api.",
            tool: DotnetBuild("GeneratedUrlShortener.Api/GeneratedUrlShortener.Api.csproj")),

        "run-tests" => Success(
            "Test run requested for GeneratedUrlShortener.Tests.",
            tool: DotnetTest("GeneratedUrlShortener.Tests/GeneratedUrlShortener.Tests.csproj")),

        "produce-final-report" => Success(
            "Final engineering report assembled.",
            new[] { new ProposedArtifact("final-report.md", "text/markdown", GreenfieldFinalReportMarkdown) }),

        _ => Fail($"No canned response for greenfield task '{request.Task.Id}'.")
    };

    private const string GreenfieldAcceptanceCriteriaMarkdown = """
        # Acceptance criteria (extracted from scenarios/greenfield/requirements.md)

        - AC1: creating a link returns a generated code and the original URL.
        - AC2: a created code resolves back to its original URL.
        - AC3: creating with an invalid (non-absolute/non-http(s)) URL is rejected.
        - AC4: looking up an unknown code reports not-found, not an exception.
        """;

    private const string GreenfieldTaskPlanMarkdown = """
        # Task plan

        analyze-requirements -> decompose-and-validate-dependencies
        inspect-workspace -> decompose-and-validate-dependencies
        decompose-and-validate-dependencies -> implement-feature -> build-solution -> run-tests -> produce-final-report
        """;

    private const string GreenfieldImplementationSummaryMarkdown = """
        # Implementation summary

        Generated a minimal 3-project solution under the run workspace:
        - GeneratedUrlShortener.Core (ShortLink, LinkStore)
        - GeneratedUrlShortener.Api (minimal API: POST /links, GET /{code})
        - GeneratedUrlShortener.Tests (xunit)
        """;

    private const string GreenfieldFinalReportMarkdown = """
        # Greenfield scenario — final report

        All tasks completed. See the engine's WorkflowExecutionReport (report.json/report.md in this
        run directory) for per-task status, decisions, risks, assumptions, and limitations.
        """;
}
