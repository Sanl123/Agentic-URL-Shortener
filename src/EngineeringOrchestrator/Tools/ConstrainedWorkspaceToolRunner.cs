using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EngineeringOrchestrator.Abstractions.Interfaces;
using EngineeringOrchestrator.Abstractions.Tools;

namespace EngineeringOrchestrator.Tools;

/// <summary>
/// Executes a small, fixed set of named operations — never an arbitrary shell string — constrained
/// to a single writable workspace directory (plus explicitly allow-listed read-only source roots
/// for fixture copies). Build/test tools shell out to the real <c>dotnet</c> CLI and return its
/// actual exit code and output; nothing here fabricates a result.
/// </summary>
public sealed class ConstrainedWorkspaceToolRunner : IToolRunner
{
    private readonly string _workspaceRoot;
    private readonly TimeSpan _processTimeout;

    public ConstrainedWorkspaceToolRunner(string workspaceRoot, TimeSpan? processTimeout = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(_workspaceRoot);
        _processTimeout = processTimeout ?? TimeSpan.FromMinutes(5);
    }

    public async Task<ToolExecutionResult> RunAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        try
        {
            return invocation.ToolName switch
            {
                "write-files" => WriteFiles(invocation),
                "list-directory" => ListDirectory(invocation),
                "read-file" => ReadFile(invocation),
                "dotnet-build" => await RunDotnetAsync("build", invocation, cancellationToken).ConfigureAwait(false),
                "dotnet-test" => await RunDotnetAsync("test", invocation, cancellationToken).ConfigureAwait(false),
                _ => new ToolExecutionResult(false, null, $"Tool '{invocation.ToolName}' has no implementation in this runner.")
            };
        }
        catch (UnauthorizedPathException ex)
        {
            return new ToolExecutionResult(false, null, ex.Message);
        }
    }

    private ToolExecutionResult WriteFiles(ToolInvocation invocation)
    {
        if (!invocation.Arguments.TryGetValue("files", out var filesJson))
        {
            return new ToolExecutionResult(false, null, "write-files requires a 'files' argument (JSON array of {path, content}).");
        }

        var entries = JsonSerializer.Deserialize<List<FileEntry>>(filesJson) ?? new List<FileEntry>();
        var written = new List<string>();

        foreach (var entry in entries)
        {
            var fullPath = ResolveWritablePath(entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, entry.Content, Encoding.UTF8);
            written.Add(entry.Path);
        }

        return new ToolExecutionResult(true, $"Wrote {written.Count} file(s): {string.Join(", ", written)}", null);
    }

    private ToolExecutionResult ListDirectory(ToolInvocation invocation)
    {
        var relativePath = invocation.Arguments.GetValueOrDefault("path", ".");
        var fullPath = ResolveReadablePath(relativePath);

        if (!Directory.Exists(fullPath))
        {
            return new ToolExecutionResult(false, null, $"Directory '{relativePath}' does not exist in the workspace.");
        }

        var entries = Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(_workspaceRoot, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return new ToolExecutionResult(true, string.Join("\n", entries), null);
    }

    private ToolExecutionResult ReadFile(ToolInvocation invocation)
    {
        if (!invocation.Arguments.TryGetValue("path", out var relativePath))
        {
            return new ToolExecutionResult(false, null, "read-file requires a 'path' argument.");
        }

        var fullPath = ResolveReadablePath(relativePath);
        if (!File.Exists(fullPath))
        {
            return new ToolExecutionResult(false, null, $"File '{relativePath}' does not exist in the workspace.");
        }

        return new ToolExecutionResult(true, File.ReadAllText(fullPath), null);
    }

    private async Task<ToolExecutionResult> RunDotnetAsync(string verb, ToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetValue("path", out var relativePath))
        {
            return new ToolExecutionResult(false, null, $"dotnet-{verb} requires a 'path' argument (project/solution relative to the workspace).");
        }

        var fullPath = ResolveReadablePath(relativePath);
        if (!File.Exists(fullPath))
        {
            return new ToolExecutionResult(false, null, $"Target '{relativePath}' does not exist in the workspace.");
        }

        var extraArgs = invocation.Arguments.GetValueOrDefault("arguments", string.Empty);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = _workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(verb);
        startInfo.ArgumentList.Add(fullPath);
        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            foreach (var arg in extraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_processTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ToolExecutionResult(false, null, $"dotnet {verb} timed out after {_processTimeout}.");
        }

        var output = $"dotnet {verb} {relativePath}\nExit code: {process.ExitCode}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}";
        return new ToolExecutionResult(process.ExitCode == 0, output, process.ExitCode == 0 ? null : $"dotnet {verb} exited with code {process.ExitCode}.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort; the process may have already exited.
        }
    }

    /// <summary>Resolves a workspace-relative path for writing, rejecting any attempt to escape the workspace root.</summary>
    private string ResolveWritablePath(string relativePath) => ResolveWithinRoot(relativePath, _workspaceRoot);

    private string ResolveReadablePath(string relativePath) => ResolveWithinRoot(relativePath, _workspaceRoot);

    private static string ResolveWithinRoot(string relativePath, string root)
    {
        var combined = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        if (!combined.Equals(root, StringComparison.OrdinalIgnoreCase) && !combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedPathException($"Path '{relativePath}' resolves outside the allowed workspace root.");
        }

        return combined;
    }

    private sealed record FileEntry(string Path, string Content);

    private sealed class UnauthorizedPathException : Exception
    {
        public UnauthorizedPathException(string message) : base(message)
        {
        }
    }
}
