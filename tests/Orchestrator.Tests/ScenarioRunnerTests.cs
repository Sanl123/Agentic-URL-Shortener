using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Scenarios;

namespace Orchestrator.Tests;

/// <summary>
/// End-to-end tests that invoke <see cref="ScenarioRunner"/> against the real scenario manifests
/// checked into <c>scenarios/</c>. These genuinely shell out to <c>dotnet build</c>/<c>dotnet test</c>
/// (no network, no API key, no live model) and are slower than typical unit tests as a result.
/// </summary>
public class ScenarioRunnerTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 10; i++)
            {
                if (File.Exists(Path.Combine(dir, "AgenticUrlShortener.sln")))
                {
                    return dir;
                }

                dir = Path.GetFullPath(Path.Combine(dir, ".."));
            }

            throw new InvalidOperationException("Could not locate repository root (AgenticUrlShortener.sln) from " + AppContext.BaseDirectory);
        }
    }

    private static string ManifestPath(string scenarioId) =>
        Path.Combine(RepoRoot, "scenarios", scenarioId, "scenario.json");

    [Fact]
    public async Task Greenfield_RealRun_SucceedsAndCompletesAllTasks()
    {
        var runner = new ScenarioRunner();

        var result = await runner.RunAsync(ManifestPath("greenfield"), CancellationToken.None);

        Assert.Equal(ScenarioOutcome.Success, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(WorkflowStatus.Completed, result.EngineReport.FinalStatus);
        Assert.All(result.EngineReport.TaskReports, t => Assert.Equal(WorkflowTaskStatus.Completed, t.FinalStatus));
        Assert.True(File.Exists(result.ReportJsonPath));
        Assert.True(File.Exists(result.ReportMarkdownPath));
    }

    [Fact]
    public async Task Brownfield_RealRun_FailsFirstThenFixesViaReplanAndSucceeds()
    {
        var runner = new ScenarioRunner();
        var fixtureShortLinkPath = Path.Combine(RepoRoot, "scenarios", "brownfield", "fixture", "LegacyUrlShortener", "ShortLink.cs");
        var originalFixtureContent = await File.ReadAllTextAsync(fixtureShortLinkPath);

        var result = await runner.RunAsync(ManifestPath("brownfield"), CancellationToken.None);

        Assert.Equal(ScenarioOutcome.Success, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(WorkflowStatus.Completed, result.EngineReport.FinalStatus);

        // The replanned/fixed workflow's tasks are what's in the final report.
        Assert.Contains(result.EngineReport.TaskReports, t => t.TaskId == "rerun-tests-after-fix" && t.FinalStatus == WorkflowTaskStatus.Completed);

        // The checked-in fixture must be untouched — all work happened in the isolated run workspace.
        var fixtureContentAfterRun = await File.ReadAllTextAsync(fixtureShortLinkPath);
        Assert.Equal(originalFixtureContent, fixtureContentAfterRun);

        // Evidence that the initial run genuinely failed before the fix was found on disk too.
        var initialReportPath = Path.Combine(result.RunDirectory, "report.initial.json");
        Assert.True(File.Exists(initialReportPath));
        var initialReportText = await File.ReadAllTextAsync(initialReportPath);
        Assert.Contains("run-tests-after-expiry-change", initialReportText);
    }

    [Fact]
    public async Task Ambiguous_RealRun_PausesSafelyWithNeedsClarificationAndExitCodeZero()
    {
        var runner = new ScenarioRunner();

        var result = await runner.RunAsync(ManifestPath("ambiguous"), CancellationToken.None);

        Assert.Equal(ScenarioOutcome.NeedsClarification, result.Outcome);
        Assert.Equal(0, result.ExitCode);

        // The engine's own raw status is allowed to be Failed — that's exactly why the scenario
        // layer exists — but the analysis/report tasks must have genuinely completed.
        Assert.Contains(result.EngineReport.TaskReports, t => t.TaskId == "analyze-ambiguous-requirement" && t.FinalStatus == WorkflowTaskStatus.Completed);
        Assert.Contains(result.EngineReport.TaskReports, t => t.TaskId == "produce-ambiguity-report" && t.FinalStatus == WorkflowTaskStatus.Completed);

        // Material implementation tasks must NOT have completed.
        Assert.Contains(result.EngineReport.TaskReports, t => t.TaskId == "propose-performance-and-analytics-improvements" && t.FinalStatus != WorkflowTaskStatus.Completed);
        Assert.Contains(result.EngineReport.TaskReports, t => t.TaskId == "deploy-performance-improvements" && t.FinalStatus != WorkflowTaskStatus.Completed);
    }

    [Fact]
    public async Task RunAsync_UnknownManifestPath_ThrowsRatherThanFabricatingAResult()
    {
        var runner = new ScenarioRunner();
        var missingPath = Path.Combine(RepoRoot, "scenarios", "does-not-exist", "scenario.json");

        // The underlying directory/file genuinely doesn't exist; .NET may raise either
        // FileNotFoundException or DirectoryNotFoundException depending on which path segment is
        // missing — both derive from IOException and both mean "fail loudly, don't fabricate".
        await Assert.ThrowsAnyAsync<IOException>(() => runner.RunAsync(missingPath, CancellationToken.None));
    }
}
