using System.Text.Json;
using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Scenarios;

public sealed partial class ScenarioFakeModelProvider
{
    private static readonly JsonSerializerOptions AmbiguityJsonOptions = new() { WriteIndented = true };

    private static TaskDecompositionResponse AmbiguousPlan()
    {
        var tasks = new WorkflowTaskDefinition[]
        {
            new()
            {
                Id = "analyze-ambiguous-requirement",
                Name = "Analyze the requirement for ambiguity before proposing any change",
                RiskLevel = RiskLevel.Low,
                ExpectedArtifacts = new[] { "ambiguity-analysis.json" }
            },
            new()
            {
                Id = "inspect-current-implementation",
                Name = "Inspect the current redirect/analytics implementation (read-only)",
                RiskLevel = RiskLevel.Low,
                DependsOn = new[] { "analyze-ambiguous-requirement" }
            },
            new()
            {
                Id = "propose-performance-and-analytics-improvements",
                Name = "Propose concrete performance and analytics changes",
                RiskLevel = RiskLevel.Critical,
                DependsOn = new[] { "analyze-ambiguous-requirement", "inspect-current-implementation" }
            },
            new()
            {
                Id = "deploy-performance-improvements",
                Name = "Apply the proposed performance and analytics changes",
                RiskLevel = RiskLevel.Critical,
                DependsOn = new[] { "propose-performance-and-analytics-improvements" }
            },
            new()
            {
                Id = "produce-ambiguity-report",
                Name = "Produce a report explaining why implementation was paused",
                RiskLevel = RiskLevel.Low,
                DependsOn = new[] { "analyze-ambiguous-requirement" },
                ExpectedArtifacts = new[] { "ambiguity-report.md" }
            }
        };

        return new TaskDecompositionResponse(
            tasks,
            "Ambiguous plan: analyze for ambiguity and inspect read-only first; material implementation " +
            "decisions (propose/deploy) are gated behind an approval that requires recorded human clarification.");
    }

    private static TaskExecutionResponse AmbiguousTaskResponse(TaskExecutionRequest request) => request.Task.Id switch
    {
        "analyze-ambiguous-requirement" => Success(
            "Requirement analyzed: it is materially ambiguous. No implementation decision can be made safely.",
            new[] { new ProposedArtifact("ambiguity-analysis.json", "application/json", JsonSerializer.Serialize(BuildAmbiguityAnalysis(), AmbiguityJsonOptions)) }),

        "inspect-current-implementation" => Success(
            "Read-only inspection of the current workspace completed; no changes made.",
            tool: ListDirectory(".")),

        "propose-performance-and-analytics-improvements" => Success(
            "A concrete performance/analytics change would be proposed here once clarification is recorded.",
            new[] { new ProposedArtifact("proposed-changes/NOTE.md", "text/markdown", "# Proposed changes\n\nNot generated: this task requires an approved clarification before any concrete proposal is made.") },
            tool: WriteFiles("[{\"Path\":\"proposed-changes/NOTE.md\",\"Content\":\"Not generated: requires approved clarification.\"}]")),

        "deploy-performance-improvements" => Success(
            "Deployment would apply the proposed changes once approved.",
            tool: WriteFiles("[{\"Path\":\"deployed/NOTE.md\",\"Content\":\"Not deployed: requires approved clarification.\"}]")),

        "produce-ambiguity-report" => Success(
            "Ambiguity report produced; implementation was safely paused pending clarification.",
            new[] { new ProposedArtifact("ambiguity-report.md", "text/markdown", AmbiguityReportMarkdown) }),

        _ => Fail($"No canned response for ambiguous task '{request.Task.Id}'.")
    };

    private static AmbiguityAnalysis BuildAmbiguityAnalysis() => new(
        RequirementSummary: "Make redirects fast and analytics accurate.",
        UnresolvedQuestions: new[]
        {
            "What is the target redirect latency, and how will it be measured (p50/p95/p99, at which tier)?",
            "What does \"accurate\" mean for analytics — exact counts, bounded error margin, or eventual reconciliation?",
            "Should click recording happen synchronously on the redirect path, or asynchronously out-of-band?",
            "Is eventual consistency between a redirect and its recorded analytics event acceptable, and for how long?",
            "What data-loss window (if any) is acceptable for analytics events during a failure?",
            "What is the expected traffic volume/scale this needs to support?",
            "Is changing infrastructure or the existing database schema permitted to achieve this, or must it stay within current constraints?"
        },
        UnapprovedAssumptions: new[]
        {
            "That synchronous click recording could be relaxed to asynchronous without stakeholder sign-off.",
            "That a specific latency target (e.g. p95 < 100ms) could be assumed without confirmation.",
            "That schema or infrastructure changes are in scope without explicit permission."
        },
        ImpactIfAssumed: "Choosing a latency target, a consistency model, or a schema/infrastructure change without " +
                         "confirmation could silently change data durability guarantees or require irreversible migrations.",
        RiskIfAssumed: "High: analytics undercounting or redirect data loss could go undetected until after deployment.",
        ApprovalStatus: "NotRecorded",
        PauseReason: "Material implementation decisions (propose/deploy) require human clarification/approval that has not been recorded; the run paused safely instead of guessing.");

    private const string AmbiguityReportMarkdown = """
        # Ambiguity report

        The requirement "Make redirects fast and analytics accurate." does not specify enough to make
        a material implementation decision safely. See `ambiguity-analysis.json` for the 7 unresolved
        questions and the unapproved assumptions that would otherwise have been required.

        Implementation tasks that would change redirect or analytics behavior were routed through an
        approval gate. No recorded human clarification/approval was available, so those tasks were
        rejected and their dependents were blocked — this is the intended, safe outcome for this
        scenario, not an execution failure.
        """;
}
