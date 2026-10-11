using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Scenarios;

public sealed partial class ScenarioFakeModelProvider
{
    private const string FixtureTestsProject = "LegacyUrlShortener.Tests/LegacyUrlShortener.Tests.csproj";

    private static TaskDecompositionResponse BrownfieldInitialPlan()
    {
        var tasks = new WorkflowTaskDefinition[]
        {
            new()
            {
                Id = "inspect-fixture",
                Name = "Inspect the existing legacy fixture before planning",
                RiskLevel = RiskLevel.Low,
                ExpectedArtifacts = new[] { "fixture-inspection.md" }
            },
            new()
            {
                Id = "run-baseline-tests",
                Name = "Run the fixture's baseline test suite",
                RiskLevel = RiskLevel.Low,
                DependsOn = new[] { "inspect-fixture" }
            },
            new()
            {
                Id = "decompose-expiry-plan",
                Name = "Decompose the expiry feature into implementation steps",
                RiskLevel = RiskLevel.Low,
                DependsOn = new[] { "run-baseline-tests" },
                ExpectedArtifacts = new[] { "expiry-plan.md" }
            },
            new()
            {
                Id = "add-expiry-field-and-check",
                Name = "Add an optional expiry field and an expiry check to the legacy store",
                RiskLevel = RiskLevel.Medium,
                DependsOn = new[] { "decompose-expiry-plan" },
                AcceptanceCriteria = new[]
                {
                    "Expired links resolve to not-found",
                    "Still-valid (non-expired) links continue to resolve",
                    "Links created without an expiry continue to resolve (no regression)"
                }
            },
            new()
            {
                Id = "build-after-expiry-change",
                Name = "Build the fixture after the expiry change",
                RiskLevel = RiskLevel.Medium,
                DependsOn = new[] { "add-expiry-field-and-check" }
            },
            new()
            {
                Id = "run-tests-after-expiry-change",
                Name = "Run the fixture's tests after the expiry change",
                RiskLevel = RiskLevel.Medium,
                DependsOn = new[] { "build-after-expiry-change" },
                MaxRetries = 1
            }
        };

        return new TaskDecompositionResponse(tasks, "Brownfield plan: inspect, baseline-test, decompose, implement expiry, build, test.");
    }

    private static TaskDecompositionResponse BrownfieldFixPlan()
    {
        var tasks = new WorkflowTaskDefinition[]
        {
            new()
            {
                Id = "fix-expiry-check-bug",
                Name = "Fix the inverted expiry-check condition",
                RiskLevel = RiskLevel.Medium
            },
            new()
            {
                Id = "rebuild-after-fix",
                Name = "Rebuild the fixture after the fix",
                RiskLevel = RiskLevel.Medium,
                DependsOn = new[] { "fix-expiry-check-bug" }
            },
            new()
            {
                Id = "rerun-tests-after-fix",
                Name = "Re-run the fixture's tests after the fix",
                RiskLevel = RiskLevel.Medium,
                DependsOn = new[] { "rebuild-after-fix" }
            }
        };

        return new TaskDecompositionResponse(tasks, "Brownfield re-plan: a genuine test failure was observed; fix the identified bug, rebuild, and re-run tests.");
    }

    private static TaskExecutionResponse BrownfieldTaskResponse(TaskExecutionRequest request) => request.Task.Id switch
    {
        "inspect-fixture" => Success(
            "Inspected the legacy fixture; it has no expiry concept today.",
            new[] { new ProposedArtifact("fixture-inspection.md", "text/markdown", "# Fixture inspection\n\nLegacyUrlShortener has ShortLink (code, originalUrl, createdAtUtc) and LinkStore (Add/TryGet). No expiry field exists yet.") },
            tool: ListDirectory(".")),

        "run-baseline-tests" => Success(
            "Baseline test run requested before any change is made.",
            tool: DotnetTest(FixtureTestsProject)),

        "decompose-expiry-plan" => Success(
            "Expiry feature decomposed into: add nullable ExpiresAtUtc, add expiry check in TryGet, add new tests, keep old call sites compiling.",
            new[] { new ProposedArtifact("expiry-plan.md", "text/markdown", "# Expiry plan\n\n- Add ShortLink.ExpiresAtUtc (nullable)\n- Add LinkStore.Add overload accepting expiry\n- Add expiry check to TryGet\n- Preserve existing 1-arg TryGet(code) and 3-arg Add(...) call sites\n- Add new tests: expired, valid-future-expiry, no-expiry regression") }),

        "add-expiry-field-and-check" => Success(
            "Implemented expiry support in the legacy fixture (isolated workspace copy only).",
            tool: WriteFiles(GeneratedFileSets.BrownfieldAddExpiryBuggyFilesJson())),

        "build-after-expiry-change" => Success(
            "Build requested after the expiry change.",
            tool: DotnetBuild(FixtureTestsProject)),

        "run-tests-after-expiry-change" => Success(
            "Test run requested after the expiry change.",
            tool: DotnetTest(FixtureTestsProject)),

        "fix-expiry-check-bug" => Success(
            "Corrected the inverted expiry-check condition in LinkStore.TryGet.",
            tool: WriteFiles(GeneratedFileSets.BrownfieldFixExpiryBugFilesJson())),

        "rebuild-after-fix" => Success(
            "Build requested after the fix.",
            tool: DotnetBuild(FixtureTestsProject)),

        "rerun-tests-after-fix" => Success(
            "Test run requested after the fix.",
            tool: DotnetTest(FixtureTestsProject)),

        _ => Fail($"No canned response for brownfield task '{request.Task.Id}'.")
    };
}
