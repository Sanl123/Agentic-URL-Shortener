namespace EngineeringOrchestrator.Abstractions.Tasks;

/// <summary>
/// A typed unit of engineering work proposed or scheduled by the orchestrator. Immutable —
/// re-planning produces new definitions rather than mutating existing ones.
/// </summary>
public sealed record WorkflowTaskDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedArtifacts { get; init; } = Array.Empty<string>();
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Low;

    /// <summary>Maximum number of execution attempts (1 = no retries).</summary>
    public int MaxRetries { get; init; } = 1;

    public TimeSpan? Timeout { get; init; }
}

/// <summary>A named, versionable set of tasks forming the DAG for one workflow run.</summary>
public sealed record WorkflowDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<WorkflowTaskDefinition> Tasks { get; init; } = Array.Empty<WorkflowTaskDefinition>();
}
