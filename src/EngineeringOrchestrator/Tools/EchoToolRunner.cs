using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.Tools;

namespace EngineeringOrchestrator.Tools;

/// <summary>
/// A safe, side-effect-free <see cref="IToolRunner"/> that always succeeds. Useful for exercising
/// the allow-list policy end-to-end in tests/demos without granting any real capability.
/// </summary>
public sealed class EchoToolRunner : IToolRunner
{
    public Task<ToolExecutionResult> RunAsync(ToolInvocation invocation, CancellationToken cancellationToken) =>
        Task.FromResult(new ToolExecutionResult(true, $"Echo: {invocation.ToolName}({string.Join(", ", invocation.Arguments.Select(a => $"{a.Key}={a.Value}"))})", null));
}
