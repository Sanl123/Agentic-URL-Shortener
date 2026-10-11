namespace EngineeringOrchestrator.Scenarios;

/// <summary>Known, hardcoded scenario identifiers the fake provider recognizes. A real provider
/// would have no such switch — it would reason over the requirement text directly.</summary>
public static class ScenarioIds
{
    public const string Greenfield = "greenfield";
    public const string Brownfield = "brownfield";
    public const string Ambiguous = "ambiguous";
}
