using System.Text.Json;
using EngineeringOrchestrator.Abstractions.Execution;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Approvals;
using EngineeringOrchestrator.Execution;
using EngineeringOrchestrator.Policies;
using EngineeringOrchestrator.Stores;
using EngineeringOrchestrator.Tools;

namespace EngineeringOrchestrator.Scenarios;

/// <summary>
/// Composition root that reuses the existing <see cref="WorkflowEngine"/> to execute one scenario
/// manifest end to end: load manifest, prepare an isolated run workspace (optionally seeded from a
/// fixture), decompose and validate the task graph via the deterministic fake provider, execute it
/// for real (real tool invocations, real <c>dotnet build</c>/<c>dotnet test</c>), re-plan and re-run
/// genuinely failed tasks where the engine's existing re-planning mechanism applies, persist every
/// artifact/report to disk, and compute a scenario-level outcome distinct from the engine's raw
/// workflow status.
/// </summary>
public sealed class ScenarioRunner
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    public async Task<ScenarioRunResult> RunAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var manifestFullPath = Path.GetFullPath(manifestPath);
        var manifest = ScenarioManifest.LoadFromFile(manifestFullPath);
        var manifestDirectory = Path.GetDirectoryName(manifestFullPath)!;

        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():n}"[..26];
        var runDirectory = Path.Combine(manifestDirectory, "..", "..", "runs", manifest.ScenarioId, runId);
        runDirectory = Path.GetFullPath(runDirectory);
        var workspaceDirectory = Path.Combine(runDirectory, "workspace");
        var artifactsDirectory = Path.Combine(runDirectory, "artifacts");
        var stateDirectory = Path.Combine(runDirectory, "state");
        Directory.CreateDirectory(workspaceDirectory);
        Directory.CreateDirectory(artifactsDirectory);
        Directory.CreateDirectory(stateDirectory);

        if (manifest.FixtureSourcePath is not null)
        {
            var fixtureFullPath = Path.GetFullPath(Path.Combine(manifestDirectory, manifest.FixtureSourcePath));
            CopyFixture(fixtureFullPath, workspaceDirectory);
        }

        var requirementFullPath = Path.GetFullPath(Path.Combine(manifestDirectory, manifest.RequirementsPath));
        var requirementText = File.ReadAllText(requirementFullPath);

        var modelProvider = new ScenarioFakeModelProvider(manifest.ScenarioId);
        var toolRunner = new ConstrainedWorkspaceToolRunner(workspaceDirectory);
        var toolPolicy = new AllowListToolExecutionPolicy(manifest.AllowedTools);
        var artifactStore = new FileArtifactStore(artifactsDirectory);
        var stateStore = new FileWorkflowStateStore(stateDirectory);
        IApprovalService approvalService = manifest.ApprovalMode.Equals("DenyAll", StringComparison.OrdinalIgnoreCase)
            ? new DenyAllApprovalService()
            : new AutoApprovalService();

        var engine = new WorkflowEngine(modelProvider, toolRunner, toolPolicy, artifactStore, approvalService, stateStore);

        var options = new WorkflowEngineOptions
        {
            MaxDegreeOfParallelism = manifest.MaxDegreeOfParallelism,
            ApprovalRequiredAtOrAbove = Enum.Parse<RiskLevel>(manifest.ApprovalRequiredAtOrAbove, ignoreCase: true),
            MaxTotalDuration = manifest.MaxTotalDurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null
        };

        var decomposition = await modelProvider
            .DecomposeAsync(new TaskDecompositionRequest(requirementText, Context: null), cancellationToken)
            .ConfigureAwait(false);

        var workflow = new WorkflowDefinition
        {
            Id = $"{manifest.ScenarioId}-{runId}",
            Name = manifest.Name,
            Tasks = decomposition.Tasks
        };

        var graphValidation = engine.Validate(workflow);
        if (!graphValidation.IsValid)
        {
            var failureReport = new WorkflowExecutionReport(
                workflow.Id,
                WorkflowStatus.Failed,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                Array.Empty<TaskExecutionReport>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                graphValidation.Errors);

            return await FinalizeAsync(
                manifest.ScenarioId, runId, ScenarioOutcome.Failed, 1, failureReport,
                runDirectory, workspaceDirectory, graphValidation.Errors, cancellationToken).ConfigureAwait(false);
        }

        var initialReport = await engine.ExecuteAsync(workflow, options, cancellationToken).ConfigureAwait(false);
        await WriteReportAsync(runDirectory, "report.initial", initialReport, cancellationToken).ConfigureAwait(false);

        var finalReport = initialReport;
        var hasFailedTask = initialReport.TaskReports.Any(t => t.FinalStatus == WorkflowTaskStatus.Failed);
        IReadOnlyList<WorkflowTaskDefinition> replanTasks = Array.Empty<WorkflowTaskDefinition>();

        if (hasFailedTask)
        {
            var replanWorkflow = await engine.ReplanFailedTasksAsync(workflow, initialReport, cancellationToken).ConfigureAwait(false);
            replanTasks = replanWorkflow.Tasks;
            if (replanWorkflow.Tasks.Count > 0)
            {
                finalReport = await engine.ExecuteAsync(replanWorkflow, options, cancellationToken).ConfigureAwait(false);
                await WriteReportAsync(runDirectory, "report.replan", finalReport, cancellationToken).ConfigureAwait(false);
            }
        }

        var taskLookup = decomposition.Tasks.Concat(replanTasks)
            .GroupBy(t => t.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var (outcome, reasons) = ComputeOutcome(manifest.ScenarioId, finalReport, taskLookup);
        var exitCode = outcome == ScenarioOutcome.Failed ? 1 : 0;

        return await FinalizeAsync(
            manifest.ScenarioId, runId, outcome, exitCode, finalReport,
            runDirectory, workspaceDirectory, reasons, cancellationToken).ConfigureAwait(false);
    }

    private static (ScenarioOutcome Outcome, IReadOnlyList<string> Reasons) ComputeOutcome(
        string scenarioId, WorkflowExecutionReport report, IReadOnlyDictionary<string, WorkflowTaskDefinition> taskLookup)
    {
        if (report.FinalStatus == WorkflowStatus.Completed)
        {
            return (ScenarioOutcome.Success, new[] { "All tasks completed successfully." });
        }

        if (scenarioId == ScenarioIds.Ambiguous)
        {
            var nonCompleted = report.TaskReports.Where(t => t.FinalStatus != WorkflowTaskStatus.Completed).ToList();
            var allGatedByMissingApproval = nonCompleted.Count > 0 && nonCompleted.All(t =>
                taskLookup.TryGetValue(t.TaskId, out var definition) &&
                definition.RiskLevel == RiskLevel.Critical &&
                t.FinalStatus is WorkflowTaskStatus.Skipped or WorkflowTaskStatus.Blocked);

            if (allGatedByMissingApproval)
            {
                return (ScenarioOutcome.NeedsClarification, new[]
                {
                    "Analysis and reporting tasks completed; the requirement was correctly identified as ambiguous.",
                    $"{nonCompleted.Count} material implementation task(s) were safely skipped/blocked pending human clarification/approval: {string.Join(", ", nonCompleted.Select(t => t.TaskId))}."
                });
            }
        }

        var failedIds = report.TaskReports.Where(t => t.FinalStatus == WorkflowTaskStatus.Failed).Select(t => t.TaskId).ToList();
        return (ScenarioOutcome.Failed, new[]
        {
            $"Workflow finished with status {report.FinalStatus}.",
            failedIds.Count > 0 ? $"Failed task(s): {string.Join(", ", failedIds)}." : "No task failed outright, but the run did not complete cleanly."
        });
    }

    private async Task<ScenarioRunResult> FinalizeAsync(
        string scenarioId, string runId, ScenarioOutcome outcome, int exitCode, WorkflowExecutionReport report,
        string runDirectory, string workspaceDirectory, IReadOnlyList<string> reasons, CancellationToken cancellationToken)
    {
        var (jsonPath, markdownPath) = await WriteReportAsync(runDirectory, "report", report, cancellationToken, outcome, reasons).ConfigureAwait(false);

        return new ScenarioRunResult(
            scenarioId, runId, outcome, exitCode, report, runDirectory, workspaceDirectory, jsonPath, markdownPath, reasons);
    }

    private static async Task<(string JsonPath, string MarkdownPath)> WriteReportAsync(
        string runDirectory, string fileStem, WorkflowExecutionReport report, CancellationToken cancellationToken,
        ScenarioOutcome? outcome = null, IReadOnlyList<string>? reasons = null)
    {
        var jsonPath = Path.Combine(runDirectory, $"{fileStem}.json");
        var markdownPath = Path.Combine(runDirectory, $"{fileStem}.md");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, ReportJsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(report, outcome, reasons), cancellationToken).ConfigureAwait(false);

        return (jsonPath, markdownPath);
    }

    private static string BuildMarkdown(WorkflowExecutionReport report, ScenarioOutcome? outcome, IReadOnlyList<string>? reasons)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Workflow report: {report.WorkflowId}");
        sb.AppendLine();
        sb.AppendLine($"- Engine status: **{report.FinalStatus}**");
        if (outcome is { } o)
        {
            sb.AppendLine($"- Scenario outcome: **{o}**");
        }
        sb.AppendLine($"- Started: {report.StartedAtUtc:O}");
        sb.AppendLine($"- Completed: {report.CompletedAtUtc:O}");
        sb.AppendLine();

        if (reasons is { Count: > 0 })
        {
            sb.AppendLine("## Outcome reasons");
            foreach (var reason in reasons)
            {
                sb.AppendLine($"- {reason}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Tasks");
        sb.AppendLine("| Task | Status | Attempts | Error |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var task in report.TaskReports)
        {
            sb.AppendLine($"| {task.TaskId} | {task.FinalStatus} | {task.Attempts} | {task.Error ?? ""} |");
        }
        sb.AppendLine();

        AppendSection(sb, "Decisions", report.Decisions);
        AppendSection(sb, "Risks", report.Risks);
        AppendSection(sb, "Assumptions", report.Assumptions);
        AppendSection(sb, "Limitations", report.Limitations);

        return sb.ToString();
    }

    private static void AppendSection(System.Text.StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine($"## {title}");
        foreach (var item in items)
        {
            sb.AppendLine($"- {item}");
        }
        sb.AppendLine();
    }

    private static void CopyFixture(string sourceDirectory, string destinationDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(s => s is "bin" or "obj"))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }
}
