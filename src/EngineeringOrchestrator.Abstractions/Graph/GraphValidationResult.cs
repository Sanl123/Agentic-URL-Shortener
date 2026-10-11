namespace EngineeringOrchestrator.Abstractions.Graph;

/// <summary>Result of validating a workflow's task graph (duplicate ids, dangling dependencies, cycles).</summary>
public sealed record GraphValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static GraphValidationResult Success { get; } = new(true, Array.Empty<string>());

    public static GraphValidationResult Failure(IReadOnlyList<string> errors) => new(false, errors);
}
