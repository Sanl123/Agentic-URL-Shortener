using EngineeringOrchestrator.Abstractions.Graph;
using EngineeringOrchestrator.Abstractions.Tasks;

namespace EngineeringOrchestrator.Graph;

/// <summary>
/// Builds and validates the dependency graph for a <see cref="WorkflowDefinition"/>: duplicate ids,
/// dangling dependency references, and cycles (via DFS with a recursion-stack color marker).
/// </summary>
public sealed class WorkflowGraph
{
    private readonly Dictionary<string, WorkflowTaskDefinition> _tasksById;

    private WorkflowGraph(Dictionary<string, WorkflowTaskDefinition> tasksById)
    {
        _tasksById = tasksById;
    }

    public IReadOnlyCollection<WorkflowTaskDefinition> Tasks => _tasksById.Values;

    public static GraphValidationResult Validate(WorkflowDefinition workflow, out WorkflowGraph? graph)
    {
        var errors = new List<string>();
        var tasksById = new Dictionary<string, WorkflowTaskDefinition>();

        foreach (var task in workflow.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                errors.Add("A task was defined with an empty Id.");
                continue;
            }

            if (!tasksById.TryAdd(task.Id, task))
            {
                errors.Add($"Duplicate task id '{task.Id}'.");
            }
        }

        foreach (var task in tasksById.Values)
        {
            foreach (var dependencyId in task.DependsOn)
            {
                if (!tasksById.ContainsKey(dependencyId))
                {
                    errors.Add($"Task '{task.Id}' depends on unknown task '{dependencyId}'.");
                }
            }
        }

        if (errors.Count > 0)
        {
            graph = null;
            return GraphValidationResult.Failure(errors);
        }

        var cycleErrors = DetectCycles(tasksById);
        if (cycleErrors.Count > 0)
        {
            graph = null;
            return GraphValidationResult.Failure(cycleErrors);
        }

        graph = new WorkflowGraph(tasksById);
        return GraphValidationResult.Success;
    }

    /// <summary>Tasks grouped into dependency "layers" — every task in layer N depends only on tasks in layers &lt; N.</summary>
    public IReadOnlyList<IReadOnlyList<string>> TopologicalLayers()
    {
        var remaining = new HashSet<string>(_tasksById.Keys);
        var completed = new HashSet<string>();
        var layers = new List<IReadOnlyList<string>>();

        while (remaining.Count > 0)
        {
            var layer = remaining
                .Where(id => _tasksById[id].DependsOn.All(completed.Contains))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            // Validate() guarantees this can't happen for an acyclic graph, but guard against misuse.
            if (layer.Count == 0)
            {
                throw new InvalidOperationException("Unable to compute topological layers: the graph contains a cycle.");
            }

            foreach (var id in layer)
            {
                remaining.Remove(id);
                completed.Add(id);
            }

            layers.Add(layer);
        }

        return layers;
    }

    public WorkflowTaskDefinition GetTask(string id) => _tasksById[id];

    public IReadOnlyList<string> GetDependents(string taskId) =>
        _tasksById.Values.Where(t => t.DependsOn.Contains(taskId)).Select(t => t.Id).ToList();

    private static List<string> DetectCycles(Dictionary<string, WorkflowTaskDefinition> tasksById)
    {
        var state = new Dictionary<string, int>(); // 0 = unvisited, 1 = in-progress, 2 = done
        var errors = new List<string>();

        foreach (var id in tasksById.Keys)
        {
            if (!state.ContainsKey(id) && TryFindCycle(id, tasksById, state, new List<string>(), out var cyclePath))
            {
                errors.Add($"Cycle detected: {string.Join(" -> ", cyclePath)}.");
            }
        }

        return errors;
    }

    private static bool TryFindCycle(
        string id,
        Dictionary<string, WorkflowTaskDefinition> tasksById,
        Dictionary<string, int> state,
        List<string> path,
        out List<string> cyclePath)
    {
        state[id] = 1;
        path.Add(id);

        foreach (var dependencyId in tasksById[id].DependsOn)
        {
            if (state.TryGetValue(dependencyId, out var depState))
            {
                if (depState == 1)
                {
                    var cycleStart = path.IndexOf(dependencyId);
                    cyclePath = path.Skip(cycleStart).Append(dependencyId).ToList();
                    return true;
                }

                continue; // already fully processed, no cycle through this edge
            }

            if (TryFindCycle(dependencyId, tasksById, state, path, out cyclePath))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        state[id] = 2;
        cyclePath = new List<string>();
        return false;
    }
}
