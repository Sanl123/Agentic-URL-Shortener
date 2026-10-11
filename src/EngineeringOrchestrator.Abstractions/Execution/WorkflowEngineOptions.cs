using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Abstractions.Execution;

/// <summary>Tunables for a single <c>ExecuteAsync</c> call: concurrency, approval threshold, budgets.</summary>
public sealed record WorkflowEngineOptions
{
    public static WorkflowEngineOptions Default { get; } = new();

    /// <summary>Maximum number of tasks the scheduler will run concurrently.</summary>
    public int MaxDegreeOfParallelism { get; init; } = 4;

    /// <summary>Tasks at or above this risk level pause for approval before they run.</summary>
    public RiskLevel ApprovalRequiredAtOrAbove { get; init; } = RiskLevel.High;

    /// <summary>Applied to a task when it does not define its own <see cref="WorkflowTaskDefinition.Timeout"/>.</summary>
    public TimeSpan DefaultTaskTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Hard ceiling on total wall-clock time for the whole workflow run (safe-stop budget).</summary>
    public TimeSpan? MaxTotalDuration { get; init; }

    /// <summary>Delay before a transient failure is retried.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(50);
}
