using EngineeringOrchestrator.Abstractions.Tools;

namespace EngineeringOrchestrator.Abstractions.Interfaces;

/// <summary>
/// Application-enforced security boundary for tool execution. The engine consults this policy for
/// every <see cref="ToolInvocation"/> before calling <see cref="IToolRunner"/>, regardless of what
/// a model proposed — model output can never bypass it.
/// </summary>
public interface IToolExecutionPolicy
{
    bool IsAllowed(ToolInvocation invocation, out string? denialReason);
}
