using EngineeringOrchestrator.Abstractions.Artifacts;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.ModelProvider;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace Orchestrator.Tests.Fakes;

/// <summary>Test-only model provider that introduces a fixed artificial delay so overlapping
/// (parallel) execution windows can be observed and measured.</summary>
public sealed class DelayedConcurrencyModelProvider : IModelProvider
{
    private readonly TimeSpan _delay;
    private int _current;
    private int _maxObserved;
    private readonly object _lock = new();

    public DelayedConcurrencyModelProvider(TimeSpan delay)
    {
        _delay = delay;
    }

    public int MaxObservedConcurrency
    {
        get { lock (_lock) { return _maxObserved; } }
    }

    public Task<TaskDecompositionResponse> DecomposeAsync(TaskDecompositionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used in concurrency tests.");

    public async Task<TaskExecutionResponse> ExecuteTaskAsync(TaskExecutionRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _current++;
            _maxObserved = Math.Max(_maxObserved, _current);
        }

        try
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                _current--;
            }
        }

        return new TaskExecutionResponse(true, "done", Array.Empty<ProposedArtifact>(), null, FailureClassification.None);
    }
}
