using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenSpecStore;
using TaskManager.AppState;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// An index kept between runs (#wi213): the parsed headers, task counts, worklog "updated" stamps and the per-file stamps of a repository are saved to one file in the app's data folder.
/// The next start paints the board from it before any file is read, then verifies it with the one listing the cache does anyway and re-reads only what changed. Each "run" here is a fresh set of
/// caches and module state (what a new process has) over the same repository and the same index folder; the store's read log counts the files opened; the clock is injected; nothing waits on time.
/// </summary>
public sealed class KeepingAnIndexBetweenRunsTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);   // a bound for a hang, never waited out

    private readonly TestRepo repo = new();
    private readonly string scratch = Directory.CreateTempSubdirectory("rtm-index").FullName;
    private string IndexFolder => Path.Combine(scratch, "index");
    private readonly List<Workspace> workspaces = [];

    public void Dispose()
    {
        foreach (Workspace w in workspaces) w.Dispose();
        repo.Dispose();
        try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
    }

    /// <summary>What a new process starts with: empty caches and module state, the index folder, and a clock far enough ahead that no file counts as just written (racy).</summary>
    private int runs;
    private SettingsStore settings = null!;

    private Workspace NewRun(int clockHours)
    {
        OpenSpecStoreApi.Close();
        IsolatedModuleState.Begin();
        OpenSpecStoreModule.State.RealWatcher = false;
        Caches.S.Items.IndexFolder = IndexFolder;
        Clock(clockHours);
        settings = new SettingsStore(Path.Combine(scratch, $"settings-{++runs}.json"));   // each run has its own settings: a project an earlier run opened is not a tab of this one
        Workspace workspace = new(settings, new UiService());
        workspaces.Add(workspace);
        return workspace;
    }

    private static DateTime Clock(int hours)
    {
        DateTime at = new(2030, 1, 1, hours, 0, 0, DateTimeKind.Utc);   // later than every file the test writes
        Caches.S.Items.Clock = () => at;
        return at;
    }

    private static async Task Load(Workspace workspace, string root)
    {
        Assert.True(workspace.Register(root).Success);
        await workspace.LoadAsync(root).WaitAsync(Limit);
    }

    private static List<string> Record()
    {
        List<string> log = [];
        OpenSpecStoreModule.State.ReadLog = log;
        return log;
    }

    /// <summary>The files of change folders that were opened, relative to openspec/changes.</summary>
    private static List<string> OpenedInChanges(List<string> log, string root)
    {
        string changes = Path.Combine(root, "openspec", "changes") + Path.DirectorySeparatorChar;
        lock (log) return [.. log.Where(p => p.StartsWith(changes, StringComparison.OrdinalIgnoreCase)).Select(p => p[changes.Length..].Replace('\\', '/'))];
    }

    private string IndexOf(string root) => IndexFile.PathFor(IndexFolder, root);

    private JsonNode ReadIndex(string root) => JsonNode.Parse(File.ReadAllText(IndexOf(root)))!;

    [Fact(DisplayName = "A second start paints the board from the saved index before any file is read")]
    public async Task ASecondStartPaintsFromTheIndex()
    {
        Workspace first = NewRun(1);
        await Load(first, repo.Root);
        int items = ItemsCache.All(includeHidden: true).Count;
        string firstTitles = string.Join('|', ItemsCache.All(includeHidden: true).Select(w => w.Id + ":" + w.Title + ":" + w.Subtasks.Count));
        Assert.True(items > 1);
        Assert.True(File.Exists(IndexOf(repo.Root)), "the first run saved the index");

        Workspace second = NewRun(2);
        List<string> log = Record();
        Assert.True(second.Register(repo.Root).Success);

        Assert.Empty(log);   // nothing was opened: not a change folder, not the config
        Assert.False(second.IsLoading(repo.Root));   // the board is drawn from the index, not as a skeleton
        Assert.Equal(items, ItemsCache.All(includeHidden: true).Count);
        Assert.Equal(firstTitles, string.Join('|', ItemsCache.All(includeHidden: true).Select(w => w.Id + ":" + w.Title + ":" + w.Subtasks.Count)));
        Assert.Equal(0, ItemsCache.FolderReads);
        Assert.Empty(log);   // reading the painted items read nothing either

        await second.LoadAsync(repo.Root).WaitAsync(Limit);   // the verification: one listing, every stamp still matches

        Assert.Equal(0, ItemsCache.FolderReads);
        Assert.DoesNotContain(OpenedInChanges(log, repo.Root), p => p.EndsWith(".openspec.yaml") || p.EndsWith("tasks.md"));
        Assert.Equal(firstTitles, string.Join('|', ItemsCache.All(includeHidden: true).Select(w => w.Id + ":" + w.Title + ":" + w.Subtasks.Count)));
    }

    [Fact(DisplayName = "A file changed while the app was closed is re-read at the start, and nothing else is")]
    public async Task OnlyTheChangedFileIsReadAtTheStart()
    {
        await Load(NewRun(1), repo.Root);
        int before = ItemsCache.All(includeHidden: true).First(w => w.Folder == "plain-change").Subtasks.Count;
        repo.Write("openspec/changes/plain-change/tasks.md", "## 1. Work\n\n- [ ] 1.1 One\n- [ ] 1.2 Two\n- [ ] 1.3 Three\n- [ ] 1.4 Four\n- [ ] 1.5 Five\n- [ ] 1.6 Six\n");   // while the app is closed

        Workspace second = NewRun(2);
        List<string> log = Record();
        Assert.True(second.Register(repo.Root).Success);
        Assert.Equal(before, ItemsCache.All(includeHidden: true).First(w => w.Folder == "plain-change").Subtasks.Count);   // painted from the index: the old count
        Assert.Empty(log);

        await second.LoadAsync(repo.Root).WaitAsync(Limit);

        Assert.Equal(1, ItemsCache.FolderReads);
        Assert.Equal(6, ItemsCache.All(includeHidden: true).First(w => w.Folder == "plain-change").Subtasks.Count);
        Assert.All(OpenedInChanges(log, repo.Root).Where(p => p.EndsWith(".openspec.yaml") || p.EndsWith("tasks.md")), p => Assert.StartsWith("plain-change/", p));
    }

    [Fact(DisplayName = "An index of a repository that moved, or of an older format version, is dropped and rebuilt")]
    public async Task AnIndexThatDoesNotFitIsDroppedAndRebuilt()
    {
        await Load(NewRun(1), repo.Root);
        int items = ItemsCache.All(includeHidden: true).Count;

        // the repository was copied elsewhere together with its index file: the file names another place
        string moved = Path.Combine(scratch, "moved-repo");
        CopyDirectory(repo.Root, moved);
        File.Copy(IndexOf(repo.Root), IndexOf(moved));
        Workspace second = NewRun(2);
        Assert.True(second.Register(moved).Success);
        Assert.True(second.IsLoading(moved));   // nothing to paint from
        Assert.False(File.Exists(IndexOf(moved)));   // dropped
        await second.LoadAsync(moved).WaitAsync(Limit);
        Assert.Equal(items, ItemsCache.FolderReads);   // every folder read: it was rebuilt
        Assert.Equal(Path.GetFullPath(moved), (string?)ReadIndex(moved)["repo"]);   // and saved for where it is

        // an index written by another version of the format
        JsonNode index = ReadIndex(moved);
        index["version"] = IndexFile.FormatVersion - 1;
        File.WriteAllText(IndexOf(moved), index.ToJsonString());
        Workspace third = NewRun(3);
        Assert.True(third.Register(moved).Success);
        Assert.True(third.IsLoading(moved));
        Assert.False(File.Exists(IndexOf(moved)));
        await third.LoadAsync(moved).WaitAsync(Limit);
        Assert.Equal(items, ItemsCache.FolderReads);
        Assert.Equal(IndexFile.FormatVersion, (int?)ReadIndex(moved)["version"]);
    }

    [Fact(DisplayName = "Rebuild index re-reads everything and writes a fresh index")]
    public async Task RebuildReadsEverythingAndWritesAFreshIndex()
    {
        await Load(NewRun(1), repo.Root);
        int items = ItemsCache.All(includeHidden: true).Count;
        string firstSaved = (string)ReadIndex(repo.Root)["savedAt"]!;

        Workspace second = NewRun(2);
        await Load(second, repo.Root);
        Assert.Equal(0, ItemsCache.FolderReads);   // the start read nothing
        List<string> log = Record();
        DateTime rebuiltAt = Clock(5);

        second.RebuildIndex(repo.Root);

        Assert.Equal(items, ItemsCache.FolderReads);
        Assert.Equal(items, OpenedInChanges(log, repo.Root).Count(p => p.EndsWith("/.openspec.yaml")));   // each folder's header was opened again
        string savedAt = (string)ReadIndex(repo.Root)["savedAt"]!;
        Assert.NotEqual(firstSaved, savedAt);
        Assert.Equal(rebuiltAt, DateTime.Parse(savedAt, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime());
        Assert.Equal(items, ReadIndex(repo.Root)["folders"]!.AsArray().Count);
    }

    [Fact(DisplayName = "The index file is portable: forward slashes, UTC stamps, no absolute paths inside the repository part")]
    public async Task TheIndexFileIsPortable()
    {
        await Load(NewRun(1), repo.Root);
        string text = File.ReadAllText(IndexOf(repo.Root));
        JsonNode index = JsonNode.Parse(text)!;

        Assert.Equal(Path.GetFullPath(repo.Root), (string?)index["repo"]);   // the one place the path is kept: to know the index is of this repository
        string folders = index["folders"]!.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.DoesNotContain(Path.GetFullPath(repo.Root), folders, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFullPath(repo.Root).Replace('\\', '/'), folders, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\\\", folders);   // no escaped backslash: no Windows path anywhere in it
        Assert.EndsWith("Z", (string)index["savedAt"]!);

        JsonNode folder = index["folders"]!.AsArray().First(f => (string?)f!["path"] == "openspec/changes/plain-change")!;
        JsonArray stamps = folder["stamps"]!.AsArray();
        Assert.NotEmpty(stamps);
        string tasks = Path.Combine(repo.Root, "openspec", "changes", "plain-change", "tasks.md");
        foreach (JsonNode? stamp in stamps)
        {
            string path = (string)stamp!["path"]!;
            Assert.DoesNotContain('\\', path);
            Assert.False(Path.IsPathRooted(path));
            if (stamp["mtime"] is { } mtime)
            {
                Assert.EndsWith("Z", (string)mtime!);
                string full = Path.Combine(repo.Root, "openspec", "changes", "plain-change", path.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(File.GetLastWriteTimeUtc(full).Ticks, DateTime.Parse((string)mtime!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime().Ticks);
                Assert.Equal(new System.IO.FileInfo(full).Length, (long)stamp["size"]!);
            }
        }
        Assert.Contains(stamps, s => (string?)s!["path"] == "tasks.md");
        Assert.True(File.Exists(tasks));
    }

    [Fact(DisplayName = "The index is written after a rebuild and on exit, never in the middle of a load")]
    public async Task TheIndexIsWrittenAtTheEndsNotInTheMiddle()
    {
        // the first run: held after the headers, the index is not there yet; it is written when the load ends
        Workspace first = NewRun(1);
        using SemaphoreSlim gate = new(0);
        first.BeforeLoadStep = (step, _) => { if (step == "git") gate.Wait(Limit); };
        Assert.True(first.Register(repo.Root).Success);
        Task load = first.LoadAsync(repo.Root);
        Assert.True(await first.WaitLoaded(Limit));   // the headers are in; the pool thread stands at the git gate
        Assert.False(File.Exists(IndexOf(repo.Root)));
        gate.Release(100);
        await load.WaitAsync(Limit);
        string firstSaved = (string)ReadIndex(repo.Root)["savedAt"]!;

        // the second run reads one changed folder at the start; the old index stays until that load ends
        repo.Write("openspec/changes/plain-change/tasks.md", "## 1. Work\n\n- [ ] 1.1 One\n- [ ] 1.2 Two\n- [ ] 1.3 Three\n- [ ] 1.4 Four\n");
        Workspace second = NewRun(2);
        using SemaphoreSlim gate2 = new(0);
        second.BeforeLoadStep = (step, _) => { if (step == "git") gate2.Wait(Limit); };
        Assert.True(second.Register(repo.Root).Success);
        Task load2 = second.LoadAsync(repo.Root);
        Assert.True(await second.WaitLoaded(Limit));
        Assert.Equal(1, ItemsCache.FolderReads);   // the changed folder was read
        Assert.Equal(firstSaved, (string)ReadIndex(repo.Root)["savedAt"]!);   // and the file on disk is still the first run's
        gate2.Release(100);
        await load2.WaitAsync(Limit);
        string secondSaved = (string)ReadIndex(repo.Root)["savedAt"]!;
        Assert.NotEqual(firstSaved, secondSaved);   // written when the load ended

        // a change after the load: the exit writes it, nothing before
        repo.Write("openspec/changes/plain-change/tasks.md", "## 1. Work\n\n- [ ] 1.1 One\n");
        OpenSpecStoreApi.Refresh();
        Assert.Single(ItemsCache.All(includeHidden: true).First(w => w.Folder == "plain-change").Subtasks);
        Assert.Equal(secondSaved, (string)ReadIndex(repo.Root)["savedAt"]!);
        DateTime exitAt = Clock(9);
        second.SaveIndexes();   // what Program calls on exit
        Assert.Equal(exitAt, DateTime.Parse((string)ReadIndex(repo.Root)["savedAt"]!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime());

        // an exit with nothing changed writes nothing
        Clock(10);
        second.SaveIndexes();
        Assert.Equal(exitAt, DateTime.Parse((string)ReadIndex(repo.Root)["savedAt"]!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime());
    }

    [Fact(DisplayName = "Settings offers Rebuild index for each open project, and pressing it reads that project again")]
    public async Task SettingsOffersRebuildIndex()
    {
        Workspace workspace = NewRun(1);
        await Load(workspace, repo.Root);
        int items = ItemsCache.All(includeHidden: true).Count;
        await using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new UiService { ToastLifetime = Timeout.InfiniteTimeSpan });
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton(workspace);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        AngleSharp.Dom.IElement row = page.Find("[data-setting='rebuild-index']");
        Assert.Contains("Rebuild index", row.QuerySelector("th")!.TextContent);
        Assert.Single(row.QuerySelectorAll("button.rebuild-index"));
        Assert.Contains(Path.GetFileName(repo.Root), row.TextContent);
        int before = ItemsCache.FolderReads;

        row.QuerySelector("button.rebuild-index")!.Click();

        page.WaitForAssertion(() => Assert.Equal(before + items, ItemsCache.FolderReads), Limit);
        Assert.True(File.Exists(IndexOf(repo.Root)));
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }
}
