using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.Tools;

namespace EngineeringOrchestrator.Policies;

/// <summary>
/// Default <see cref="IToolExecutionPolicy"/>: deny-by-default allow-list. A tool name not present
/// in <see cref="AllowedToolNames"/> is always rejected, regardless of which task or model proposed it.
/// </summary>
public sealed class AllowListToolExecutionPolicy : IToolExecutionPolicy
{
    public IReadOnlySet<string> AllowedToolNames { get; }

    public AllowListToolExecutionPolicy(IEnumerable<string> allowedToolNames)
    {
        AllowedToolNames = allowedToolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAllowed(ToolInvocation invocation, out string? denialReason)
    {
        if (AllowedToolNames.Contains(invocation.ToolName))
        {
            denialReason = null;
            return true;
        }

        denialReason = $"Tool '{invocation.ToolName}' is not on the approved allow-list.";
        return false;
    }
}
