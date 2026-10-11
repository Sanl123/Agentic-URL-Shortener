namespace EngineeringOrchestrator.Abstractions.Tools;

/// <summary>
/// A request to run a single, named, parameterized tool operation. There is deliberately no way
/// to express an arbitrary shell command here — tool runners expose a fixed, vetted operation set.
/// </summary>
public sealed record ToolInvocation(string ToolName, IReadOnlyDictionary<string, string> Arguments);

public sealed record ToolExecutionResult(bool Success, string? Output, string? ErrorMessage);
