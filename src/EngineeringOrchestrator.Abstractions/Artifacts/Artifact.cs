namespace EngineeringOrchestrator.Abstractions.Artifacts;

/// <summary>A tracked work product attributed to the task and workflow that produced it (decision lineage).</summary>
public sealed record Artifact
{
    public required string Id { get; init; }
    public required string WorkflowId { get; init; }
    public required string TaskId { get; init; }
    public required string Name { get; init; }
    public string ContentType { get; init; } = "text/plain";
    public string Content { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>An artifact proposed by a model/tool before it has been assigned an Id or persisted.</summary>
public sealed record ProposedArtifact(string Name, string ContentType, string Content);
