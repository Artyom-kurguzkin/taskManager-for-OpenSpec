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
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
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
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
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

    [Fact(DisplayName = "Changing the plans folder in Settings writes only that key")]
    public void ChangingThePlansFolderWritesOnlyThatKey()
    {
        repo.Write(Config, "# kept\nschema: spec-driven\n\nx-rtm:\n  # my comment\n  lanes:\n    In Progress: [High priority]\n  statuses: [Backlog, Todo, In Progress, Completed]\n  attachmentsDir: pics/old\n  features:\n  - billing\ncontext: |\n  The project.\n");
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        SettingsStore settings = NewSettings();

        Assert.True(settings.SetPlansDir(repo.Root, "plans").Success);
        Assert.Equal("# kept\nschema: spec-driven\n\nx-rtm:\n  # my comment\n  lanes:\n    In Progress: [High priority]\n  statuses: [Backlog, Todo, In Progress, Completed]\n  attachmentsDir: pics/old\n  features:\n  - billing\n  plansDir: plans\ncontext: |\n  The project.\n", Yaml());

        Assert.True(settings.SetPlansDir(repo.Root, "other").Success);        // the same line again, not a second one
        Assert.Contains("  plansDir: other\ncontext:", Yaml());
        Assert.Single(Yaml().Split('\n'), l => l.StartsWith("  plansDir:"));

        Assert.True(settings.SetAttachmentsDir(repo.Root, SettingsStore.DefaultAttachmentsDir).Success);   // the default removes only its own line
        Assert.DoesNotContain("attachmentsDir", Yaml());
        Assert.Contains("  statuses: [Backlog, Todo, In Progress, Completed]\n  features:\n  - billing\n  plansDir: other\n", Yaml());
    }

    [Fact(DisplayName = "The plans folder and the images folder are saved with the project one at a time, each read back on its own")]
    public void PlansAndImagesAreSavedSeparately()
    {
        SettingsStore settings = NewSettings();
        Assert.Equal(("DevResources/plans", "local/task-tracking"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));

        Assert.True(settings.SetPlansDir(repo.Root, "docs\\plans/").Success);                // tidied like the images folder
        Assert.Equal(("docs/plans", "local/task-tracking"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));   // the other did not move
        Assert.Contains("  plansDir: docs/plans\n", Yaml());
        Assert.DoesNotContain("attachmentsDir", Yaml());

        Assert.True(settings.SetAttachmentsDir(repo.Root, "media").Success);
        Assert.Equal(("docs/plans", "media"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));
        Assert.Contains("  plansDir: docs/plans\n", Yaml());                                 // and setting images left the plans line alone
        Assert.Contains("  attachmentsDir: media\n", Yaml());
        SettingsStore other = new(Path.Combine(temp, "other.json"));                          // another computer reading the same repository gets both
        Assert.Equal(("docs/plans", "media"), (other.PlansDir(repo.Root), other.AttachmentsDir(repo.Root)));
        Assert.Equal("docs/plans", NotesApi.ProjectPlansDir(repo.Root));

        Assert.True(settings.SetPlansDir(repo.Root, "").Success);                             // empty = the default, which removes only its own line
        Assert.Equal("DevResources/plans", settings.PlansDir(repo.Root));
        Assert.DoesNotContain("plansDir", Yaml());
        Assert.Contains("  attachmentsDir: media\n", Yaml());

        string before = Yaml();
        foreach (string bad in new[] { "openspec", "openspec/plans", "../outside", "C:/elsewhere", "/abs", ".", "a/<b>" })
        {
            (bool ok, string message) = settings.SetPlansDir(repo.Root, bad);
            Assert.False(ok, bad);
            Assert.NotEmpty(message);
        }
        Assert.Equal(before, Yaml());                                                         // refused: nothing changed
    }

    [Fact(DisplayName = "A repository with the old one-folder key resolves both folders from it until one of them is set")]
    public void TheOldOneFolderKeyStillResolvesBoth()
    {
        repo.Write(Config, "schema: spec-driven\n\nx-rtm:\n  filesDir: files/all\n");
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        SettingsStore settings = NewSettings();
        Assert.Equal(("files/all/plans", "files/all/images"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));

        Assert.True(settings.SetPlansDir(repo.Root, "docs/plans").Success);                   // one set: the other still comes from the old key
        Assert.Equal(("docs/plans", "files/all/images"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));
        Assert.True(settings.SetAttachmentsDir(repo.Root, "media").Success);
        Assert.Equal(("docs/plans", "media"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));
        Assert.Contains("  filesDir: files/all\n", Yaml());                                   // the old line is never written or removed by the app

        Assert.True(settings.SetAttachmentsDir(repo.Root, SettingsStore.DefaultAttachmentsDir).Success);   // the default is written when the old key would otherwise answer
        Assert.Equal("local/task-tracking", settings.AttachmentsDir(repo.Root));
        Assert.Contains("  attachmentsDir: local/task-tracking\n", Yaml());
    }

    [Fact(DisplayName = "The plans and attachments folders resolve the same way on every screen")]
    public void TheFoldersResolveTheSameEverywhere()
    {
        SettingsStore settings = NewSettings();
        Assert.Equal(OpenSpecStoreApi.DefaultAttachmentsDir, SettingsStore.DefaultAttachmentsDir);   // one default each
        Assert.Equal(OpenSpecStoreApi.DefaultAttachmentsDir, NotesApi.CheckAttachmentsDir(null).Dir);

        repo.Write(Config, "x-rtm:\n  filesDir: files/all\n");
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        Assert.Equal("files/all", NotesApi.ProjectFilesDir(repo.Root));
        Assert.Equal("files/all/plans", settings.PlansDir(repo.Root));
        Assert.Equal("files/all/images", settings.AttachmentsDir(repo.Root));

        repo.Write(Config, "x-rtm:\n  filesDir: ../outside\n  attachmentsDir: pics\n");   // not allowed: every screen falls to the next level
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        Assert.Null(NotesApi.ProjectFilesDir(repo.Root));
        Assert.Equal("pics", settings.AttachmentsDir(repo.Root));

        repo.Write(Config, "x-rtm:\n  filesDir: [broken\n");                                  // unreadable: the same
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        Assert.Null(NotesApi.ProjectFilesDir(repo.Root));
        Assert.Equal(OpenSpecStoreApi.DefaultAttachmentsDir, settings.AttachmentsDir(repo.Root));
    }

    [Fact(DisplayName = "The config.yaml path is built in one place only")]
    public void TheConfigPathIsBuiltInOneModule()
    {
        string dir = SourceFiles.Root();
        // the solution's own projects, read from the .slnx: no walk of the checkout (agent worktrees, build output, git objects)
        string[] builders = [.. SourceFiles.ProjectFolders(dir).Where(p => Path.GetFileName(p) is not ("TaskManager.Tests" or "OpenSpecStore"))
            .SelectMany(p => SourceFiles.Of(p, "*.cs", "*.razor"))
            .Where(f => File.ReadAllText(f).Contains("\"config.yaml\""))
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))];
        Assert.Empty(builders);   // the ones that build the path from pieces name it by this literal; OpenSpecStoreApi.ConfigFile is the one that may
    }
}
