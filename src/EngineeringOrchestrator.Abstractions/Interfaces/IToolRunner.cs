using EngineeringOrchestrator.Abstractions.Tools;

namespace EngineeringOrchestrator.Abstractions.Interfaces;

/// <summary>
/// Executes a single vetted tool operation. The engine is the only caller — it validates every
/// invocation against an allow-list policy first, so this interface never receives arbitrary
/// shell commands sourced directly from model output.
/// </summary>
public interface IToolRunner
{
    Task<ToolExecutionResult> RunAsync(ToolInvocation invocation, CancellationToken cancellationToken);
}
