using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngineeringOrchestrator.Scenarios;

/// <summary>
/// Describes one executable scenario: which canned plan the deterministic fake provider should
/// use, where its requirements/fixture live on disk, and how the engine should be configured for
/// the run. Loaded from a <c>scenario.json</c> file next to the scenario's requirements document.
/// </summary>
public sealed class ScenarioManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Selects the deterministic fake provider's canned plan: "greenfield", "brownfield", or "ambiguous".</summary>
    public required string ScenarioId { get; init; }

    public required string Name { get; init; }

    /// <summary>Path to the requirements document, relative to the manifest file.</summary>
    public required string RequirementsPath { get; init; }

    /// <summary>Optional path to a fixture directory copied into the run workspace before execution, relative to the manifest file.</summary>
    public string? FixtureSourcePath { get; init; }

    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();

    /// <summary>"AutoApprove" (default) or "DenyAll" (used by the ambiguous scenario to prove material
    /// decisions cannot proceed without recorded clarification/approval).</summary>
    public string ApprovalMode { get; init; } = "AutoApprove";

    public int MaxDegreeOfParallelism { get; init; } = 2;

    public string ApprovalRequiredAtOrAbove { get; init; } = "High";

    public int? MaxTotalDurationSeconds { get; init; }

    public static ScenarioManifest LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<ScenarioManifest>(json, JsonOptions);
        return manifest ?? throw new InvalidOperationException($"Scenario manifest at '{path}' deserialized to null.");
    }
}
