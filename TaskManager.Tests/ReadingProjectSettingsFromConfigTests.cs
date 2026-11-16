using LinkedTestRuns;
using Notes;
using OpenSpecStore;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// The project's settings live in openspec/config.yaml under x-rtm and are read and written through one place (OpenSpecStore), with one failure rule:
/// a section that cannot be read reads as its default and is reported, it never stops the app, and it is never written over.
/// Nothing here waits for time: every step is a call that is awaited.
/// </summary>
public sealed class ReadingProjectSettingsFromConfigTests : IDisposable
{
    private const string Config = "openspec/config.yaml";
    private static readonly string[] Defaults = ["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"];

    private readonly TestRepo repo = new();
    private readonly string temp = Directory.CreateTempSubdirectory("rtm-config-").FullName;

    public void Dispose()
    {
        repo.Dispose();
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private string Yaml() => repo.Read(Config).Replace("\r\n", "\n");

    private SettingsStore NewSettings() => new(Path.Combine(temp, "settings.json"));

    [Fact(DisplayName = "A project with a broken config.yaml section still opens with default lanes and statuses and says what is wrong")]
    public void ABrokenSectionReadsAsDefaultsAndIsReported()
    {
        repo.Write(Config, "schema: spec-driven\n\nx-rtm:\n  lanes: [not, a: mapping\n  statuses: [Todo, {broken\n  filesDir: attachments\n");
        OpenSpecStoreApi.Open(repo.Root);

        Assert.Equal(Defaults, WorkItemsApi.GetStatuses());     // the built-in list
        Assert.Empty(WorkItemsApi.GetLanes());                   // no lanes
        List<string> warnings = [.. WorkItemsApi.GetWarnings()];
        Assert.Contains(warnings, w => w.Contains("x-rtm.statuses") && w.Contains("config.yaml") && w.Contains("could not be read"));
        Assert.Contains(warnings, w => w.Contains("x-rtm.lanes") && w.Contains("config.yaml") && w.Contains("could not be read"));
        Assert.Equal("attachments", NotesApi.ProjectFilesDir(repo.Root));   // the section next to them is fine

        string before = Yaml();
        Assert.False(WorkItemsApi.AddStatus("Review ready").Success);        // never written over
        Assert.False(WorkItemsApi.SetLane(123, "In Progress", "High priority").Success);
        Assert.Equal(before, Yaml());
    }

    [Fact(DisplayName = "A section of config.yaml that is not there is no problem, and a broken one never stops the others being read")]
    public void EachSectionFailsOnItsOwn()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Null(OpenSpecStoreApi.ReadConfigSection<List<string>>("statuses").Value);
        Assert.Null(OpenSpecStoreApi.ReadConfigSection<List<string>>("statuses").Problem);
        Assert.Empty(WorkItemsApi.GetWarnings().Where(w => w.Contains("config.yaml")));

        repo.Write(Config, "x-rtm:\n  statuses: [a, {broken\n  features: [billing, banking]\n  testResults: [\"results/*.trx\"]\n");
        Assert.Null(OpenSpecStoreApi.ReadConfigSection<List<string>>("statuses").Value);
        Assert.Contains("x-rtm.statuses", OpenSpecStoreApi.ReadConfigSection<List<string>>("statuses").Problem);
        Assert.Equal(["billing", "banking"], OpenSpecStoreApi.ReadConfigSection<List<string>>("features").Value);
        Assert.Equal(["results/*.trx"], OpenSpecStoreApi.ReadConfigSection<List<string>>("testResults").Value);
        OpenSpecStoreApi.Refresh();
        Assert.Equal(["results/*.trx"], LinkedTestRunsApi.Latest().Globs);

        repo.Write(Config, "x-rtm:\n  features: [billing, {broken\n  testResults: [\"results/*.trx\", {broken\n");
        OpenSpecStoreApi.Refresh();                                          // an outside edit, noticed
        Assert.Empty(LinkedTestRunsApi.Latest().Globs);                      // the app still opens: no globs
        Assert.False(Features.FeaturesApi.CreateFeature("Functional", "new-one").Success);   // the list is not written over
        Assert.Contains("billing, {broken", Yaml());
    }

    [Fact(DisplayName = "A project's settings can be read from a folder that is not the open project")]
    public void AnotherFolderCanBeRead()
    {
        using TestRepo other = new();
        other.Write(Config, "x-rtm:\n  filesDir: other/files\n");
        OpenSpecStoreApi.Open(repo.Root);

        Assert.Equal("other/files", OpenSpecStoreApi.ReadConfigSection<string>("filesDir", other.Root).Value);
        Assert.Null(OpenSpecStoreApi.ReadConfigSection<string>("filesDir").Value);   // the open project has none
        Assert.Equal(Path.Combine(other.Root, "openspec", "config.yaml"), OpenSpecStoreApi.ConfigFile(other.Root));
    }

    [Fact(DisplayName = "Changing the files folder in Settings writes only that key")]
    public void ChangingTheFilesFolderWritesOnlyThatKey()
    {
        repo.Write(Config, "# kept\nschema: spec-driven\n\nx-rtm:\n  # my comment\n  lanes:\n    In Progress: [High priority]\n  statuses: [Backlog, Todo, In Progress, Completed]\n  attachmentsDir: pics/old\n  features:\n  - billing\ncontext: |\n  The project.\n");
        SettingsStore settings = NewSettings();

        Assert.True(settings.SetFilesDir(repo.Root, "files").Success);
        Assert.Equal("# kept\nschema: spec-driven\n\nx-rtm:\n  # my comment\n  lanes:\n    In Progress: [High priority]\n  statuses: [Backlog, Todo, In Progress, Completed]\n  attachmentsDir: pics/old\n  features:\n  - billing\n  filesDir: files\ncontext: |\n  The project.\n", Yaml());

        Assert.True(settings.SetFilesDir(repo.Root, "other").Success);        // the same line again, not a second one
        Assert.Contains("  filesDir: other\ncontext:", Yaml());
        Assert.Single(Yaml().Split('\n'), l => l.StartsWith("  filesDir:"));

        Assert.True(settings.SetAttachmentsDir(repo.Root, SettingsStore.DefaultAttachmentsDir).Success);   // the default removes only its own line
        Assert.DoesNotContain("attachmentsDir", Yaml());
        Assert.Contains("  statuses: [Backlog, Todo, In Progress, Completed]\n  features:\n  - billing\n  filesDir: other\n", Yaml());
    }

    [Fact(DisplayName = "The plans, files and attachments folders resolve the same way on every screen")]
    public void TheFoldersResolveTheSameEverywhere()
    {
        SettingsStore settings = NewSettings();
        Assert.Equal(OpenSpecStoreApi.DefaultAttachmentsDir, SettingsStore.DefaultAttachmentsDir);   // one default each
        Assert.Equal(OpenSpecStoreApi.DefaultFilesDir, SettingsStore.DefaultFilesDir);
        Assert.Equal(OpenSpecStoreApi.DefaultAttachmentsDir, NotesApi.CheckAttachmentsDir(null).Dir);
        Assert.Equal(OpenSpecStoreApi.DefaultFilesDir, NotesApi.CheckFilesDir("").Dir);

        repo.Write(Config, "x-rtm:\n  filesDir: files/all\n");
        Assert.Equal("files/all", settings.FilesDir(repo.Root));
        Assert.Equal("files/all", NotesApi.ProjectFilesDir(repo.Root));
        Assert.Equal("files/all/plans", settings.PlansDir(repo.Root));
        Assert.Equal("files/all/images", settings.AttachmentsDir(repo.Root));

        repo.Write(Config, "x-rtm:\n  filesDir: ../outside\n  attachmentsDir: pics\n");   // not allowed: every screen falls to the next level
        Assert.Null(settings.FilesDir(repo.Root));
        Assert.Null(NotesApi.ProjectFilesDir(repo.Root));
        Assert.Equal("pics", settings.AttachmentsDir(repo.Root));

        repo.Write(Config, "x-rtm:\n  filesDir: [broken\n");                                  // unreadable: the same
        Assert.Null(settings.FilesDir(repo.Root));
        Assert.Null(NotesApi.ProjectFilesDir(repo.Root));
        Assert.Equal(OpenSpecStoreApi.DefaultAttachmentsDir, settings.AttachmentsDir(repo.Root));
    }

    [Fact(DisplayName = "The config.yaml path is built in one place only")]
    public void TheConfigPathIsBuiltInOneModule()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "RepoTaskManager.slnx"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        string[] builders = [.. Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.StartsWith("TaskManager.Tests/") && !f.StartsWith(".claude/") && !f.StartsWith("OpenSpecStore/"))
            .Where(f => File.ReadAllText(Path.Combine(dir, f)).Contains("\"config.yaml\""))];
        Assert.Empty(builders);   // the ones that build the path from pieces name it by this literal; OpenSpecStoreApi.ConfigFile is the one that may
    }
}
