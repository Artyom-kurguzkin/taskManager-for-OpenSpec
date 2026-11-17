using System.IO.Pipelines;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Notes;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Mcp;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// Work item and note numbers are handed out as "highest + 1", so two branches or two computers that each create one offline get the same
/// number; after a git merge the repository holds both. The app repairs that by itself: the later one takes the next free number, what it
/// says about itself follows it, and its worklog tells what happened.
/// </summary>
public sealed class RepairingDuplicateIdsTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ids-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private const string Health = "openspec/changes/add-health-check";
    private const string Second = "openspec/changes/second-check";

    /// <summary>The fixture's #wi123 copied to <paramref name="folder"/> (as a merge brings it in), created on <paramref name="created"/>; its notes are not copied.</summary>
    private void Twin(string folder = "second-check", string created = "2026-10-20") => EditingWorkItemsTests.Duplicate(repo, folder, created);

    private static IEnumerable<string> Events(int id, string kind) => WorkItemsApi.GetEvents(id).Where(e => e.Kind == kind).Select(e => e.Text);

    /// <summary>Every file of the repository with its content and modified time: what "writes nothing" is measured against.</summary>
    private Dictionary<string, (string Text, DateTime Time)> Snapshot() =>
        Directory.EnumerateFiles(repo.OpenSpec, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => (File.ReadAllText(f), File.GetLastWriteTimeUtc(f)));

    [Fact(DisplayName = "Two work items that got the same id after a merge: the one created first keeps it and the other gets the next free id")]
    public void LaterTwinTakesNextFreeId()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Twin();

        IReadOnlyList<(int OldId, int NewId, string Folder)> renumbered = WorkItemsApi.ResolveDuplicateIds();

        Assert.Equal([(123, 126, "second-check")], renumbered);   // 124 is archived, 125 is the folder without a number: 126 is free
        Assert.Equal("add-health-check", WorkItemsApi.Get(123)!.Folder);
        Assert.Equal("second-check", WorkItemsApi.Get(126)!.Folder);
        Assert.Contains("  id: 126", repo.Read(Second + "/.openspec.yaml"));
        Assert.Contains("  id: 123", repo.Read(Health + "/.openspec.yaml"));
        Assert.Empty(WorkItemsApi.GetWarnings().Where(w => w.Contains("is used by")));
    }

    [Fact(DisplayName = "The renumbered work item's own references to its old id follow it, and other items' references stay with the keeper")]
    public void OwnReferencesFollow()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Twin();
        repo.Write(Second + "/proposal.md", repo.Read(Second + "/proposal.md") + "\nSplit out of #wi123, not of #wi1234 or #wi12.\n");
        repo.Write(Second + "/tasks.md", repo.Read(Second + "/tasks.md") + "- [ ] 2.2 Tell the team what #wi123 changed\n");
        repo.Write(Second + "/notes/n300.md", "---\nid: 300\nby: Tom\ncreated: 2026-10-21T09:00:00\n---\n# Plan\n\nThis is #wi123 and that is #wi107.\n");
        repo.Write(Second + "/events.log", "2026-10-20T10:00:00 | Tom | custom | worked on #wi123\n");
        repo.Write("openspec/changes/export-remittances/proposal.md", repo.Read("openspec/changes/export-remittances/proposal.md") + "\nBlocked by #wi123.\n");
        repo.Write(Health + "/proposal.md", repo.Read(Health + "/proposal.md") + "\nSee #wi123.\n");
        OpenSpecStoreApi.Refresh();

        WorkItemsApi.ResolveDuplicateIds();

        string proposal = repo.Read(Second + "/proposal.md");
        Assert.Contains("Split out of #wi126, not of #wi1234 or #wi12.", proposal);   // an id that only starts with the old one is left alone
        Assert.Contains("#wi126 changed", repo.Read(Second + "/tasks.md"));
        Assert.Contains("This is #wi126 and that is #wi107.", repo.Read(Second + "/notes/n300.md"));
        Assert.Contains("worked on #wi126", repo.Read(Second + "/events.log"));
        Assert.Contains("Blocked by #wi123.", repo.Read("openspec/changes/export-remittances/proposal.md"));   // another item: it most likely meant the keeper
        Assert.Contains("See #wi123.", repo.Read(Health + "/proposal.md"));
    }

    [Fact(DisplayName = "A renumbering is recorded in the renumbered item's worklog, naming the old id, the new id and who kept it")]
    public void RenumberingIsInTheWorklog()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Twin();
        WorkItemsApi.ConfigureActor(() => "Tester");
        try { WorkItemsApi.ResolveDuplicateIds(); }
        finally { WorkItemsApi.ConfigureActor(null); }

        WorkItemEvent logged = Assert.Single(WorkItemsApi.GetEvents(126), e => e.Kind == "renumbered");
        Assert.Equal("Tester", logged.By);
        Assert.Equal("#wi123 -> #wi126: the id was taken by \"Health-check endpoint\" (add-health-check) after a merge", logged.Text);
        Assert.Empty(Events(123, "renumbered"));   // the keeper's log is not touched
    }

    [Fact(DisplayName = "Two notes that got the same id after a merge: the one created first keeps it, the other's file and frontmatter get the next free id")]
    public void LaterNoteTwinTakesNextFreeId()
    {
        const string NoteTwin = "openspec/changes/export-remittances/notes/n123.md";
        repo.Write(NoteTwin, "---\nid: 123\nby: Tom\ncreated: 2026-10-05T10:00:00\n---\n# Twin note\n\nSee #n123 and #wi107.\n");   // created later than the fixture's note
        OpenSpecStoreApi.Open(repo.Root);

        IReadOnlyList<(int OldId, int NewId, string Path)> renumbered = NotesApi.RepairDuplicateIds();

        Assert.Equal([(123, 124, repo.PathOf("openspec/changes/export-remittances/notes/n124.md"))], renumbered);
        Assert.False(File.Exists(repo.PathOf(NoteTwin)));
        Assert.Equal(123, NotesApi.Get(123)!.WorkItemId);   // the older note keeps the number and its file
        Assert.Equal(107, NotesApi.Get(124)!.WorkItemId);
        Assert.Equal("---\nid: 124\nby: Tom\ncreated: 2026-10-05T10:00:00\n---\n# Twin note\n\nSee #n124 and #wi107.\n", repo.Read("openspec/changes/export-remittances/notes/n124.md"));   // its own reference to itself follows
        Assert.Contains("id: 123", repo.Read(Health + "/notes/n123.md"));
        Assert.Empty(NotesApi.RepairDuplicateIds());   // said once

        WorkItemEvent logged = Assert.Single(WorkItemsApi.GetEvents(107), e => e.Kind == "renumbered");
        Assert.Equal("#n123 -> #n124: the id was taken by \"Ping test\" (add-health-check) after a merge", logged.Text);
    }

    [Fact(DisplayName = "A renumbered note keeps the paths of its attachments, which are listed in its frontmatter and still resolve")]
    public void RenumberedNoteKeepsAttachmentPaths()
    {
        repo.Write("openspec/changes/export-remittances/notes/n123.md", "---\nid: 123\nby: Tom\ncreated: 2026-10-05T10:00:00\nattachments: [local/task-tracking/wi107/n123/shot.png]\n---\n# Twin note\n");
        repo.Write("local/task-tracking/wi107/n123/shot.png", "png");
        OpenSpecStoreApi.Open(repo.Root);

        NotesApi.RepairDuplicateIds();

        Assert.Equal(["local/task-tracking/wi107/n123/shot.png"], NotesApi.Get(124)!.Attachments);   // the files are not moved: a path in the list is what the note shows
        Assert.True(File.Exists(repo.PathOf("local/task-tracking/wi107/n123/shot.png")));
    }

    [Fact(DisplayName = "An archived item keeps its id over an open item created at the same time")]
    public void ArchivedKeepsOverOpenAtTheSameTime()
    {
        OpenSpecStoreApi.Open(repo.Root);
        string archived = "openspec/changes/archive/2026-10-01-old-check";
        repo.Write(archived + "/.openspec.yaml", "schema: spec-driven\ncreated: 2026-09-12\nx-rtm:\n  id: 123\n  title: Old check\n");
        repo.Write(archived + "/proposal.md", "## Why\n\nold\n");

        IReadOnlyList<(int OldId, int NewId, string Folder)> renumbered = WorkItemsApi.ResolveDuplicateIds();

        Assert.Equal([(123, 126, "add-health-check")], renumbered);
        Assert.Equal("Old check", WorkItemsApi.Get(123)!.Title);
        Assert.Equal("Health-check endpoint", WorkItemsApi.Get(126)!.Title);
    }

    [Fact(DisplayName = "Three work items with one id: the first keeps it and the others get two different new ids")]
    public void ThreeTwins()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Twin("second-check", "2026-10-20");
        Twin("third-check", "2026-10-25");

        IReadOnlyList<(int OldId, int NewId, string Folder)> renumbered = WorkItemsApi.ResolveDuplicateIds();

        Assert.Equal([(123, 126, "second-check"), (123, 127, "third-check")], renumbered);
        Assert.Equal(["add-health-check", "second-check", "third-check"], new[] { 123, 126, 127 }.Select(id => WorkItemsApi.Get(id)!.Folder));
    }

    [Fact(DisplayName = "With no duplicate ids the repair changes nothing and writes nothing")]
    public void NothingToRepairWritesNothing()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Dictionary<string, (string Text, DateTime Time)> before = Snapshot();
        long version = OpenSpecStoreApi.Version;

        Assert.Empty(WorkItemsApi.ResolveDuplicateIds());
        Assert.Empty(NotesApi.RepairDuplicateIds());

        Assert.Equal(before, Snapshot());   // not even the number the folder without one shows provisionally
        Assert.Equal(version, OpenSpecStoreApi.Version);
    }

    [Fact(DisplayName = "Repairing twice at once renumbers each duplicate once and logs it once")]
    public void ConcurrentRepairsRenumberOnce()
    {
        OpenSpecStoreApi.Open(repo.Root);
        Twin();

        List<(int, int, string)>[] results = [.. Enumerable.Range(0, 4).AsParallel().WithDegreeOfParallelism(4).Select(_ => WorkItemsApi.ResolveDuplicateIds().ToList())];

        Assert.Equal(1, results.Sum(r => r.Count));
        Assert.Single(Events(126, "renumbered"));
        Assert.Equal(2, WorkItemsApi.GetAll(true).Count(w => w.Id is 123 or 126));
        Assert.Equal(WorkItemsApi.GetAll(true).Count, WorkItemsApi.GetAll(true).Select(w => w.Id).Distinct().Count());
    }

    [Fact(DisplayName = "A duplicate whose yaml does not parse is left as it is")]
    public void UnparsableTwinIsLeftAlone()
    {
        OpenSpecStoreApi.Open(repo.Root);
        const string broken = "schema: spec-driven\ncreated: 2026-10-30\nx-rtm:\n  id: 123\n  features: [unclosed\n  title: Broken\n";
        repo.Write("openspec/changes/broken-twin/.openspec.yaml", broken);

        Assert.Empty(WorkItemsApi.ResolveDuplicateIds());
        Assert.Equal(broken, repo.Read("openspec/changes/broken-twin/.openspec.yaml"));
        Assert.Empty(Events(123, "renumbered"));
    }

    [Fact(DisplayName = "Opening a repository that holds duplicate ids shows every item once, each under its own id")]
    public void OpeningRepairs()
    {
        WorkItemsApi.ConfigureContributors(null);
        Twin();
        using Workspace workspace = new(new SettingsStore(settingsFile), new UiService());

        Assert.True(workspace.Open(repo.Root).Success);

        IReadOnlyList<WorkItem> all = WorkItemsApi.GetAll(includeHidden: true);
        Assert.Equal(all.Count, all.Select(w => w.Id).Distinct().Count());
        Assert.Equal(5, all.Count);   // the fixture's four and the copy
        Assert.Equal("add-health-check", WorkItemsApi.Get(123)!.Folder);
        Assert.Equal("second-check", WorkItemsApi.Get(126)!.Folder);
        Assert.Empty(WorkItemsApi.GetWarnings().Where(w => w.Contains("is used by")));
    }

    [Fact(DisplayName = "Opening a repository that holds two notes with one id shows both, each under its own id")]
    public void OpeningRepairsNotes()
    {
        WorkItemsApi.ConfigureContributors(null);
        repo.Write("openspec/changes/export-remittances/notes/n123.md", "---\nid: 123\nby: Tom\ncreated: 2026-10-05T10:00:00\n---\n# Twin note\n");
        using Workspace workspace = new(new SettingsStore(settingsFile), new UiService());

        Assert.True(workspace.Open(repo.Root).Success);

        Assert.Equal([123, 124], NotesApi.GetAll().Select(n => n.Id).Order());
        Assert.Single(Events(107, "renumbered"));
    }

    [Fact(DisplayName = "A reload after outside edits that introduce a duplicate id repairs it once and does not loop")]
    public void ReloadRepairsOnceAndDoesNotLoop()
    {
        WorkItemsApi.ConfigureContributors(null);
        using Workspace workspace = new(new SettingsStore(settingsFile), new UiService());
        Assert.True(workspace.Open(repo.Root).Success);
        int scans = WorkItemsModule.DuplicateScans;

        Twin();   // a git pull brings a folder in ...
        repo.Write("openspec/changes/export-remittances/notes/n123.md", "---\nid: 123\nby: Tom\ncreated: 2026-10-05T10:00:00\n---\n# Twin note\n");   // ... and a note into a folder that was there
        OpenSpecStoreApi.Refresh();
        workspace.Notify(); workspace.Resolved.Wait();   // what the file watcher's callback does

        Assert.Equal(scans + 1, WorkItemsModule.DuplicateScans);
        Assert.Equal("second-check", WorkItemsApi.Get(126)!.Folder);
        Assert.Single(Events(126, "renumbered"));
        Assert.Single(Events(107, "renumbered"));
        string log = repo.Read(Second + "/events.log");

        for (int i = 0; i < 3; i++) { workspace.Notify(); workspace.Resolved.Wait(); OpenSpecStoreApi.Refresh(); workspace.Notify(); workspace.Resolved.Wait(); }   // the app's own writes come back as changes

        Assert.Equal(scans + 1, WorkItemsModule.DuplicateScans);
        Assert.Equal(log, repo.Read(Second + "/events.log"));
        Assert.Single(Events(107, "renumbered"));
        Assert.Equal([123, 124, 125], NotesApi.GetAll().Select(n => n.Id).Order());   // 124 is the note that says #wi126 was renumbered, 125 the note twin
    }

    [Fact(DisplayName = "The board tools see the repaired ids: a duplicate made outside shows once under each id")]
    public async Task BoardToolsSeeRepairedIds()
    {
        Twin();
        ServiceProvider services = new ServiceCollection().AddLogging().AddMcpServer().AddBoardTools().Services.BuildServiceProvider();
        Pipe toServer = new(), toClient = new();
        McpServer server = McpServer.Create(
            new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()),
            services.GetRequiredService<IOptions<McpServerOptions>>().Value,
            services.GetRequiredService<ILoggerFactory>(), services);
        _ = server.RunAsync();
        await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
        try
        {
            CallToolResult list = await client.CallToolAsync("board_list", new Dictionary<string, object?> { ["repo"] = repo.Root });
            int[] ids = [.. list.StructuredContent!.Value.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32())];
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Contains(126, ids);

            CallToolResult get = await client.CallToolAsync("board_get", new Dictionary<string, object?> { ["repo"] = repo.Root, ["id"] = 126 });
            JsonElement item = get.StructuredContent!.Value;
            Assert.Equal("second-check", item.GetProperty("folder").GetString());
            Assert.Contains(item.GetProperty("worklog").EnumerateArray(), e => e.GetProperty("kind").GetString() == "renumbered");
        }
        finally
        {
            await server.DisposeAsync();
            await services.DisposeAsync();
        }
    }

    [Fact(DisplayName = "The worklog shows a renumbering among the item's events, and hiding events hides it")]
    public async Task WorklogShowsTheRenumbering()
    {
        await using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        WorkItemsApi.ConfigureContributors(null);
        Twin();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        UiService ui = ctx.Services.GetRequiredService<UiService>();

        IRenderedComponent<Worklog> log = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(126)!).Add(c => c.Root, repo.Root));

        List<string> kinds() => log.FindAll(".wk-e").Select(e => e.GetAttribute("data-kind")!).ToList();
        Assert.Contains("renumbered", kinds());
        Assert.Contains("#wi123 → #wi126", log.Find(".wk-e[data-kind='renumbered'] .wk-line").TextContent.Replace("->", "→"));

        ui.FilterWorklog(new WorklogFilters(Edits: false));   // not housekeeping: it says what happened to the item
        log.WaitForAssertion(() => Assert.Contains("renumbered", kinds()));

        ui.FilterWorklog(new WorklogFilters(Events: false));
        log.WaitForAssertion(() => Assert.DoesNotContain("renumbered", kinds()));
    }
}
