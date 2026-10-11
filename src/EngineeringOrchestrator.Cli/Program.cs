using EngineeringOrchestrator.Scenarios;

if (args.Length < 2 || !string.Equals(args[0], "scenario", StringComparison.OrdinalIgnoreCase) || !string.Equals(args[1], "run", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: dotnet run --project <cli-project> -- scenario run <path-to-scenario.json>");
    return 2;
}

if (args.Length < 3)
{
    Console.Error.WriteLine("Missing scenario manifest path.");
    return 2;
}

var manifestPath = args[2];
if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"Scenario manifest not found: {manifestPath}");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var runner = new ScenarioRunner();

try
{
    var result = await runner.RunAsync(manifestPath, cts.Token);

    Console.WriteLine();
    Console.WriteLine($"Scenario:   {result.ScenarioId}");
    Console.WriteLine($"Run id:     {result.RunId}");
    Console.WriteLine($"Outcome:    {result.Outcome}");
    Console.WriteLine($"Engine status: {result.EngineReport.FinalStatus}");
    Console.WriteLine($"Run directory:  {result.RunDirectory}");
    Console.WriteLine($"Workspace:      {result.WorkspaceDirectory}");
    Console.WriteLine($"Report (JSON):  {result.ReportJsonPath}");
    Console.WriteLine($"Report (MD):    {result.ReportMarkdownPath}");
    Console.WriteLine();
    Console.WriteLine("Outcome reasons:");
    foreach (var reason in result.OutcomeReasons)
    {
        Console.WriteLine($"  - {reason}");
    }

    return result.ExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Scenario run failed with an unhandled exception: {ex.Message}");
    Console.Error.WriteLine(ex.ToString());
    return 1;
}
