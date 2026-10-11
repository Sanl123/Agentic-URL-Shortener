using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Approvals;
using EngineeringOrchestrator.Execution;
using EngineeringOrchestrator.ModelProviders;
using EngineeringOrchestrator.Policies;
using EngineeringOrchestrator.Stores;
using EngineeringOrchestrator.Tools;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests;

/// <summary>Wires up a <see cref="WorkflowEngine"/> with in-memory stores and deterministic fakes for tests.</summary>
public sealed class WorkflowEngineTestHarness
{
    public DeterministicFakeModelProvider ModelProvider { get; } = new();
    public InMemoryArtifactStore ArtifactStore { get; } = new();
    public InMemoryWorkflowStateStore StateStore { get; } = new();
    public ScriptedApprovalService ApprovalService { get; }
    public EchoToolRunner ToolRunner { get; } = new();
    public AllowListToolExecutionPolicy ToolPolicy { get; }
    public WorkflowEngine Engine { get; }

    public WorkflowEngineTestHarness(bool defaultApproval = true, IEnumerable<string>? allowedTools = null)
    {
        ApprovalService = new ScriptedApprovalService(defaultApproval);
        ToolPolicy = new AllowListToolExecutionPolicy(allowedTools ?? new[] { "echo" });
        Engine = new WorkflowEngine(ModelProvider, ToolRunner, ToolPolicy, ArtifactStore, ApprovalService, StateStore);
    }
}
