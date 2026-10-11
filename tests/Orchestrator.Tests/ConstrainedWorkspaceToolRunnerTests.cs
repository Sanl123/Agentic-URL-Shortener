using System.Text.Json;
using EngineeringOrchestrator.Abstractions.Tools;
using EngineeringOrchestrator.Tools;

namespace Orchestrator.Tests;

public class ConstrainedWorkspaceToolRunnerTests : IDisposable
{
    private readonly string _workspaceRoot;

    public ConstrainedWorkspaceToolRunnerTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "sw-tool-runner-tests-" + Guid.NewGuid().ToString("n"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private sealed record FileEntry(string Path, string Content);

    [Fact]
    public async Task WriteFiles_WritesRealFilesUnderWorkspaceRoot()
    {
        var runner = new ConstrainedWorkspaceToolRunner(_workspaceRoot);
        var filesJson = JsonSerializer.Serialize(new[] { new FileEntry("sub/dir/hello.txt", "hello world") });

        var result = await runner.RunAsync(
            new ToolInvocation("write-files", new Dictionary<string, string> { ["files"] = filesJson }), CancellationToken.None);

        Assert.True(result.Success);
        var writtenPath = Path.Combine(_workspaceRoot, "sub", "dir", "hello.txt");
        Assert.True(File.Exists(writtenPath));
        Assert.Equal("hello world", await File.ReadAllTextAsync(writtenPath));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../escape.txt")]
    [InlineData("sub/../../escape.txt")]
    public async Task WriteFiles_PathTraversalAttempt_IsRejectedWithoutWritingAnything(string maliciousPath)
    {
        var runner = new ConstrainedWorkspaceToolRunner(_workspaceRoot);
        var filesJson = JsonSerializer.Serialize(new[] { new FileEntry(maliciousPath, "should never land on disk") });

        var result = await runner.RunAsync(
            new ToolInvocation("write-files", new Dictionary<string, string> { ["files"] = filesJson }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        var escapedFullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, maliciousPath));
        Assert.False(File.Exists(escapedFullPath));
    }

    [Fact]
    public async Task ReadFile_PathTraversalAttempt_IsRejected()
    {
        var runner = new ConstrainedWorkspaceToolRunner(_workspaceRoot);

        var result = await runner.RunAsync(
            new ToolInvocation("read-file", new Dictionary<string, string> { ["path"] = "../outside.txt" }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ListDirectory_ReturnsRelativePathsOfWrittenFiles()
    {
        var runner = new ConstrainedWorkspaceToolRunner(_workspaceRoot);
        var filesJson = JsonSerializer.Serialize(new[]
        {
            new FileEntry("a.txt", "a"),
            new FileEntry("nested/b.txt", "b")
        });
        await runner.RunAsync(new ToolInvocation("write-files", new Dictionary<string, string> { ["files"] = filesJson }), CancellationToken.None);

        var result = await runner.RunAsync(new ToolInvocation("list-directory", new Dictionary<string, string> { ["path"] = "." }), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("a.txt", result.Output);
        Assert.Contains("nested/b.txt", result.Output!.Replace('\\', '/'));
    }

    [Fact]
    public async Task ReadFile_MissingFile_FailsCleanlyRatherThanThrowing()
    {
        var runner = new ConstrainedWorkspaceToolRunner(_workspaceRoot);

        var result = await runner.RunAsync(
            new ToolInvocation("read-file", new Dictionary<string, string> { ["path"] = "does-not-exist.txt" }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task UnknownToolName_ReturnsFailureResult_NotAnException()
    {
        var runner = new ConstrainedWorkspaceToolRunner(_workspaceRoot);

        var result = await runner.RunAsync(
            new ToolInvocation("run-arbitrary-shell", new Dictionary<string, string> { ["cmd"] = "rm -rf /" }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task DotnetBuild_MissingProjectFile_FailsCleanlyWithoutSpawningDotnet()
    {
        var runner = new ConstrainedWorkspaceToolRunner(_workspaceRoot);

        var result = await runner.RunAsync(
            new ToolInvocation("dotnet-build", new Dictionary<string, string> { ["path"] = "NoSuchProject/NoSuchProject.csproj" }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DotnetBuild_RealMinimalProject_ActuallyInvokesDotnetAndSucceeds()
    {
        var runner = new ConstrainedWorkspaceToolRunner(_workspaceRoot, processTimeout: TimeSpan.FromMinutes(2));
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """;
        var filesJson = JsonSerializer.Serialize(new[]
        {
            new FileEntry("Tiny/Tiny.csproj", csproj),
            new FileEntry("Tiny/Class1.cs", "namespace Tiny; public class Class1 { }")
        });
        await runner.RunAsync(new ToolInvocation("write-files", new Dictionary<string, string> { ["files"] = filesJson }), CancellationToken.None);

        var result = await runner.RunAsync(
            new ToolInvocation("dotnet-build", new Dictionary<string, string> { ["path"] = "Tiny/Tiny.csproj" }), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Exit code: 0", result.Output);
    }
}
