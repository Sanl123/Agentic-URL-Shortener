using EngineeringOrchestrator.Abstractions.Execution;

namespace EngineeringOrchestrator.Scenarios;

/// <summary>The full result of one scenario run, including where its evidence was persisted on disk.</summary>
public sealed record ScenarioRunResult(
    string ScenarioId,
    string RunId,
    ScenarioOutcome Outcome,
    int ExitCode,
    WorkflowExecutionReport EngineReport,
    string RunDirectory,
    string WorkspaceDirectory,
    string ReportJsonPath,
    string ReportMarkdownPath,
    IReadOnlyList<string> OutcomeReasons);
