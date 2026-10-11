using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace Orchestrator.Tests.Fakes;

/// <summary>Test-only model provider whose first attempt takes longer than a configured per-task
/// timeout (to exercise timeout-as-transient-failure handling) and whose subsequent attempts
/// return immediately, so a retry after a timeout can succeed deterministically.</summary>
public sealed class SlowFirstAttemptModelProvider : IModelProvider
{
    private readonly TimeSpan _firstAttemptDelay;

    public SlowFirstAttemptModelProvider(TimeSpan firstAttemptDelay)
    {
        _firstAttemptDelay = firstAttemptDelay;
    }

    public Task<TaskDecompositionResponse> DecomposeAsync(TaskDecompositionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used in timeout tests.");

    public async Task<TaskExecutionResponse> ExecuteTaskAsync(TaskExecutionRequest request, CancellationToken cancellationToken)
    {
        if (request.AttemptNumber == 1)
        {
            // Honors the caller's token, so the engine's per-task timeout CTS can cut this short.
            await Task.Delay(_firstAttemptDelay, cancellationToken).ConfigureAwait(false);
        }

        return new TaskExecutionResponse(true, "done", Array.Empty<ProposedArtifact>(), null, FailureClassification.None);
    }
}
