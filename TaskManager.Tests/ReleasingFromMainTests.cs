using System.Text.Json;

namespace TaskManager.Tests;

/// <summary>The release contract the self-updater depends on: every push to main publishes a GitHub release
/// whose version comes from version.json and whose asset is named exactly RepoTaskManager-win-x64.zip.</summary>
public sealed class ReleasingFromMainTests
{
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "RepoTaskManager.slnx"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("RepoTaskManager.slnx not found above " + AppContext.BaseDirectory);
    }

    private static string Workflow() => File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"));

    [Fact]
    public void Public_releases_are_cut_from_main_only()
    {
        using JsonDocument version = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "version.json")));
        string[] specs = version.RootElement.GetProperty("publicReleaseRefSpec").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["^refs/heads/main$"], specs);
        Assert.Matches(@"^\d+\.\d+$", version.RootElement.GetProperty("version").GetString()!);
    }

    [Fact]
    public void Every_project_gets_its_version_from_Nerdbank_GitVersioning()
    {
        string props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
        Assert.Contains("Nerdbank.GitVersioning", props);
    }

    [Fact]
    public void The_workflow_runs_on_every_push_to_main()
    {
        string yml = Workflow();
        Assert.Matches(@"(?m)^on:\s*\r?\n\s+push:\s*\r?\n\s+branches:\s*\[main\]", yml);
        Assert.Contains("contents: write", yml);
    }

    [Fact]
    public void The_workflow_publishes_the_asset_the_updater_downloads()
    {
        string yml = Workflow();
        Assert.Contains("RepoTaskManager-win-x64.zip", yml);
        Assert.Contains("gh release create", yml);
        Assert.Contains("nbgv get-version -v SemVer2", yml);
        Assert.Contains("-p:PublishProfile=dist", yml);
    }

    [Fact]
    public void The_debug_throw_component_takes_its_address_in_release_builds_too()
    {
        // MainLayout always passes Uri; a Release build without the parameter fails on every page.
        string razor = File.ReadAllText(Path.Combine(RepoRoot(), "TaskManager", "Components", "Shared", "DebugThrow.razor"));
        int parameter = razor.IndexOf("[Parameter] public string Uri", StringComparison.Ordinal);
        int debugOnly = razor.IndexOf("#if DEBUG", StringComparison.Ordinal);
        Assert.True(parameter >= 0 && (debugOnly < 0 || parameter < debugOnly), "Uri must be declared outside #if DEBUG");
    }

    [Fact]
    public void The_workflow_does_not_run_the_tests()
    {
        Assert.DoesNotContain("dotnet test", Workflow());
    }
}
