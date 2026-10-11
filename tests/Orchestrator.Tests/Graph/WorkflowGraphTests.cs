using EngineeringOrchestrator.Abstractions.Tasks;
using EngineeringOrchestrator.Graph;

namespace Orchestrator.Tests.Graph;

public class WorkflowGraphTests
{
    private static WorkflowTaskDefinition Task(string id, params string[] dependsOn) => new()
    {
        Id = id,
        Name = id,
        DependsOn = dependsOn
    };

    [Fact]
    public void Validate_WithAcyclicGraph_Succeeds()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf1",
            Name = "Linear",
            Tasks = new[] { Task("a"), Task("b", "a"), Task("c", "b") }
        };

        var result = WorkflowGraph.Validate(workflow, out var graph);

        Assert.True(result.IsValid);
        Assert.NotNull(graph);
    }

    [Fact]
    public void Validate_WithDirectCycle_ReportsCycleError()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf2",
            Name = "Cycle",
            Tasks = new[] { Task("a", "b"), Task("b", "a") }
        };

        var result = WorkflowGraph.Validate(workflow, out var graph);

        Assert.False(result.IsValid);
        Assert.Null(graph);
        Assert.Contains(result.Errors, e => e.Contains("Cycle detected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WithLongerCycle_ReportsCycleError()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf3",
            Name = "Longer cycle",
            Tasks = new[] { Task("a", "c"), Task("b", "a"), Task("c", "b") }
        };

        var result = WorkflowGraph.Validate(workflow, out _);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Cycle detected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WithUnknownDependency_ReportsError()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf4",
            Name = "Dangling",
            Tasks = new[] { Task("a", "does-not-exist") }
        };

        var result = WorkflowGraph.Validate(workflow, out _);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown task", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WithDuplicateIds_ReportsError()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf5",
            Name = "Duplicates",
            Tasks = new[] { Task("a"), Task("a") }
        };

        var result = WorkflowGraph.Validate(workflow, out _);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate task id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TopologicalLayers_ForDiamond_GroupsIndependentTasksTogether()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf6",
            Name = "Diamond",
            Tasks = new[] { Task("a"), Task("b", "a"), Task("c", "a"), Task("d", "b", "c") }
        };

        WorkflowGraph.Validate(workflow, out var graph);
        var layers = graph!.TopologicalLayers();

        Assert.Equal(3, layers.Count);
        Assert.Equal(new[] { "a" }, layers[0]);
        Assert.Equal(new[] { "b", "c" }, layers[1]);
        Assert.Equal(new[] { "d" }, layers[2]);
    }
}
