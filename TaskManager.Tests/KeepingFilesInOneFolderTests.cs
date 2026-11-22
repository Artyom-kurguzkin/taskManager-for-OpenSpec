using System.Diagnostics;
using BoardSync;
using BoardSync.PublicDTOs;
using Bunit;
using GitHistory;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Mcp;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// A project keeps its plans and its images (what notes and descriptions attach) in folders of their own, each saved with the project (openspec/config.yaml, x-rtm.plansDir and
/// x-rtm.attachmentsDir) and each with its own line in Settings. A repository that still has the old one-folder key (x-rtm.filesDir: images and plans inside it) resolves both from it
/// until one of them is set. Nothing here waits for time: every step is a call or a click that is awaited.
/// </summary>
public sealed class KeepingFilesInOneFolderTests : IDisposable
{
    private const string OldImages = "local/task-tracking";
    private const string OldPlans = "DevResources/plans";
    private const string Item123 = "openspec/changes/add-health-check/.openspec.yaml";
    private const string Item124 = "openspec/changes/archive/2026-09-18-session-timeout/.openspec.yaml";
    private const string NoteFile = "openspec/changes/add-health-check/notes/n123.md";
    private const string Proposal = "openspec/changes/add-health-check/proposal.md";
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"/></svg>";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly TestRepo repo = new();
    private readonly string temp = Directory.CreateTempSubdirectory("rtm-files-").FullName;
    private readonly List<string> links = [];
    private readonly List<IDisposable> disposables = [];
    private readonly List<TestRepo> others = [];

    public void Dispose()
    {
        foreach (string link in links) try { if (Directory.Exists(link)) Directory.Delete(link); } catch (IOException) { }   // the link only, never what it points to
        foreach (IDisposable d in disposables) d.Dispose();
        NotesApi.SetAttachmentsDir(repo.Root, null);
        repo.Dispose();
        foreach (TestRepo r in others) r.Dispose();
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ── helpers ──

    private string SettingsFile => Path.Combine(temp, "settings.json");

    private SettingsStore NewSettings() => new(SettingsFile);

    private static string Unix(string text) => text.Replace("\r\n", "\n");

    private string Config() => Unix(repo.Read("openspec/config.yaml"));

    private void Bytes(string rel, params byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(repo.PathOf(rel))!);
        File.WriteAllBytes(repo.PathOf(rel), bytes.Length == 0 ? Png : bytes);
    }

    private void Note(string file, int id, string[] attachments, string body) =>
        repo.Write(file, $"---\nid: {id}\nby: Artyom\ncreated: 2026-10-02T09:14:00\n" + (attachments.Length > 0 ? "attachments:\n" + string.Concat(attachments.Select(a => $"- {a}\n")) : "") + $"---\n{body}");

    /// <summary>The old layout with something in every place: two images, two plans (one with a drawing beside it) linked from an open and an archived item, and the links to them.</summary>
    private void Legacy()
    {
        Bytes($"{OldImages}/wi123/n123/shot.png", 1);
        Bytes($"{OldImages}/wi123/pasted-1.png", 2);
        Note(NoteFile, 123, [$"{OldImages}/wi123/n123/shot.png"], $"# Ping test\n\nThe plan is [[{OldPlans}/export-2026-11-09]] and [the drawing]({OldPlans}/drawing.excalidraw.svg).\n");
        repo.Write(Proposal, $"# Proposal\n\n![]({OldImages}/wi123/pasted-1.png)\n\nSee [[{OldPlans}/export-2026-11-09.md]].\n");
        repo.Write($"{OldPlans}/export-2026-11-09.md", "# Export plan\n\n![the drawing](drawing.excalidraw.svg)\n\nThen [the old plan](old-2026-09-01.md).\n");
        repo.Write($"{OldPlans}/drawing.excalidraw.svg", Svg);
        repo.Write($"{OldPlans}/old-2026-09-01.md", "# Old plan\n");
        repo.Write(Item123, Unix(repo.Read(Item123)).Replace("  title: Health-check endpoint\n", $"  title: Health-check endpoint\n  plan: {OldPlans}/export-2026-11-09.md\n"));
        repo.Write(Item124, Unix(repo.Read(Item124)).Replace("  title: Session timeout after 30 min\n", $"  title: Session timeout after 30 min\n  plan: {OldPlans}/old-2026-09-01.md\n"));
    }

    /// <summary>What a repository has that still carries the old one-folder key. The app no longer writes it, so it is written here, the way a pull or a hand edit would; the workspace is told when there is one.</summary>
    private void OneFolder(string dir, BunitContext? ctx = null)
    {
        Assert.True(OpenSpecStoreApi.WriteConfigValue("filesDir", dir, repo.Root).Success);
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        if (ctx?.Services.GetRequiredService<Workspace>() is { } workspace) { workspace.Notify(); workspace.Resolved.Wait(); }   // the scan and the guides check run after Changed, on the pool
    }

    private BunitContext NewContext(SettingsStore? settings = null, bool storage = false, TestRepo? second = null, FolderPicker? picker = null)
    {
        BunitContext ctx = new();
        disposables.Add(ctx);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        if (picker is not null) ctx.Services.AddSingleton(picker);
        UiService ui = new() { ToastLifetime = Timeout.InfiniteTimeSpan };
        ctx.Services.AddSingleton(ui);
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(settings ?? NewSettings());
        if (storage)
        {
            BoardStorageSettings board = new(Path.Combine(temp, "data"), StorageProviders.Builtin);
            ctx.Services.AddSingleton(board);
            BoardSyncService sync = new(board, ui, new SyncSetup(TimeSpan.FromMinutes(2), async o => await BoardSyncApi.Sync(o)));
            disposables.Add(sync);
            ctx.Services.AddSingleton(sync);
        }
        ctx.Services.AddSingleton<Workspace>();
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        Assert.True(workspace.Open(repo.Root).Success);
        if (second is not null) Assert.True(workspace.OpenTab(second.Root).Success);
        return ctx;
    }

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<SettingsPage> page, string kind) => page.Find($"[data-project] [data-folder='{kind}'] input");

    private static Task Type(IRenderedComponent<SettingsPage> page, string kind, string value)
    {
        page.ChangeWhenReady($"[data-project] [data-folder='{kind}'] input", value);
        return Task.CompletedTask;
    }

    private static string[] Actions(IRenderedComponent<SettingsPage> page, string kind) => [.. page.FindAll($"[data-project] [data-folder='{kind}'] .att-actions button").Select(b => b.TextContent.Trim())];

    private static Task Move(IRenderedComponent<SettingsPage> page, string kind) => Click(page, $"[data-folder='{kind}'] .folder-move");

    private static Task Point(IRenderedComponent<SettingsPage> page, string kind) => Click(page, $"[data-folder='{kind}'] .folder-point");

    private static Task Click(IRenderedComponent<SettingsPage> page, string selector)
    {
        page.ClickWhenReady(selector);
        return Task.CompletedTask;
    }

    /// <summary>The item page's "linked" view, where the plan and its link picker are.</summary>
    private static Task LinkedView(IRenderedComponent<WorkItemPage> page)
    {
        page.ChangeWhenReady(".item-views input[value='linked']", "linked");
        return Task.CompletedTask;
    }

    private void Junction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using Process p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(10000);
        Assert.True(Directory.Exists(link), "the junction could not be made");
        links.Add(link);
    }

    // ── the settings page: what is gone, what is grouped ──

    [Fact(DisplayName = "The settings have no default board view any more, and old settings files still load")]
    public async Task NoDefaultBoardView()
    {
        File.WriteAllText(SettingsFile, "{ \"Theme\": \"dark\", \"DefaultView\": \"list\", \"Hints\": true }");   // a file written before the setting went

        SettingsStore settings = NewSettings();

        Assert.Equal("dark", settings.Current.Theme);                                     // the rest of the file is read
        Assert.True(settings.Current.Hints);
        Assert.Null(typeof(AppSettings).GetProperty("DefaultView"));
        Assert.Equal("board", settings.Board(repo.Root).View);                            // a repository opened for the first time shows the board, whatever the old file said
        settings.Update(s => s with { Theme = "light" });
        Assert.DoesNotContain("DefaultView", File.ReadAllText(SettingsFile));             // and the value is not written back

        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.DoesNotContain("Default board view", page.Markup);
        Assert.Empty(page.FindAll("[name='defview'], [data-name='defview']"));
        Assert.Contains("Keyboard hints", page.Markup);                                   // its neighbours are still there
    }

    [Fact(DisplayName = "A project's tab is a table of rows, one setting each: Location, Plans, Images, Board storage, Statuses, in that order, under the project's name written once")]
    public async Task ProjectTabIsATableInOrder()
    {
        TestRepo second = new();
        others.Add(second);
        await using BunitContext ctx = NewContext(storage: true, second: second);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        for (int project = 0; project < 2; project++)
        {
            page.OnProject(project);
            Assert.Single(page.FindAll("[data-project]:not([data-storage])"));                // one project's rows at a time
            Assert.Single(page.FindAll("[data-storage]"));
            Assert.Equal(1, page.FindAll(".settings-panel h3").Count);                        // the project's name is written once
            Assert.Equal(Path.GetFileName(project == 0 ? repo.Root : second.Root), page.Find(".settings-panel h3").TextContent.Trim());
            Assert.Equal(["Location", "Plans", "Images", "Board storage"], page.FindAll(".settings-panel table.set-table > tbody > tr > th").Take(4).Select(h => h.TextContent.Trim()));
            Assert.Equal(["Statuses"], page.FindAll(".settings-panel [data-statuses] > tr > th").Select(h => h.TextContent.Trim()));
            Assert.Equal(["tbody", "tbody", "tbody"], page.FindAll(".settings-panel table.set-table > tbody").Select(b => b.LocalName));   // folders, board storage, statuses: the groups of one table
            Assert.Empty(page.FindAll(".settings-panel section, .settings-panel .card"));      // no cards, no frames
        }
        Assert.Empty(page.OnTab("general").FindAll("[data-project]"));                        // the General tab has none of them
    }

    // ── the model: one files folder, two fixed parts ──

    [Fact(DisplayName = "A repository with the old one-folder key keeps images in its images folder and plans in its plans folder")]
    public async Task ImagesAndPlansInside()
    {
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        OneFolder("attachments", ctx);

        Assert.Equal("attachments/images", settings.AttachmentsDir(repo.Root));
        Assert.Equal("attachments/plans", settings.PlansDir(repo.Root));
        Assert.Equal(Path.GetFullPath(repo.PathOf("attachments/images")), NotesApi.AttachmentsRoot(repo.Root));   // the Notes module was told
        Assert.True(NotesApi.AttachBytes(123, "x.png", Png).Success);
        Assert.True(NotesApi.AttachItemImage(repo.Root, 123, "pasted.png", Png).Success);
        Assert.True(File.Exists(repo.PathOf("attachments/images/wi123/n123/x.png")));
        Assert.True(File.Exists(repo.PathOf("attachments/images/wi123/pasted.png")));
        Assert.False(Directory.Exists(repo.PathOf(OldImages)));

        repo.Write("attachments/plans/new-2026-11-13.md", "# New\n");
        repo.Write($"{OldPlans}/elsewhere-2026-11-13.md", "# Elsewhere\n");
        IRenderedComponent<WorkItemPage> item = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        await LinkedView(item);
        item.ClickWhenReady(".related button[aria-label='Link a plan']");
        string[] offered = [.. item.FindAll(".menu-pop .pick-item").Select(e => e.TextContent.Trim())];
        item.Eventually(() => Assert.Contains("new-2026-11-13.md", offered));                                    // the picker lists the project's plans folder...
        item.Eventually(() => Assert.DoesNotContain("elsewhere-2026-11-13.md", offered));                        // ...and no other
    }

    [Fact(DisplayName = "Only the images folder is kept out of git; plans stay tracked")]
    public async Task OnlyImagesIgnored()
    {
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        OneFolder("attachments", ctx);

        Assert.True(NotesApi.AttachBytes(123, "x.png", Png).Success);

        string[] lines = [.. Unix(repo.Read(".gitignore")).Split('\n').Select(l => l.Trim())];
        Assert.Contains("attachments/images/", lines);
        Assert.DoesNotContain("attachments/", lines);                                     // the whole files folder is not ignored
        Assert.DoesNotContain("attachments", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("attachments/plans"));
        Assert.DoesNotContain(lines, l => l == "local/" || l == "local");                  // and the old folder is not added by the new layout
    }

    [Fact(DisplayName = "A project without the old one-folder key keeps this computer's folders, and one with it resolves both from it until one of them is set")]
    public void OldKeyStillRead()
    {
        SettingsStore settings = NewSettings();
        string key = Path.GetFullPath(repo.Root).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        settings.Update(s => s with { PlansDirs = new() { [key] = "mine/plans" }, AttachmentsDirs = new() { [key] = "mine/pics" } });

        // no key at all: this computer's folders, exactly as before; nothing is written or moved by itself
        Assert.Equal(("mine/plans", "mine/pics"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));
        Assert.DoesNotContain("filesDir", Config());

        OneFolder("files\\all/");                                                        // an old repository, or a pull that brought the old key

        Assert.Equal(("files/all/plans", "files/all/images"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));   // tidied, and both from it
        Assert.Equal("files/all", NotesApi.ProjectFilesDir(repo.Root));
        SettingsStore other = new(SettingsFile + ".other");                               // another computer reading the same repository gets the same folders
        Assert.Equal(("files/all/plans", "files/all/images"), (other.PlansDir(repo.Root), other.AttachmentsDir(repo.Root)));

        Assert.True(settings.SetPlansDir(repo.Root, "docs/plans").Success);               // one set on its own: the other still comes from the old key
        Assert.Equal(("docs/plans", "files/all/images"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));
        Assert.Null(settings.Current.PlansDirs);                                          // this computer's plans folder is cleared for the repository...
        Assert.NotNull(settings.Current.AttachmentsDirs);                                 // ...and its images folder is not touched
        Assert.Contains("  plansDir: docs/plans\n", Config());
        Assert.Contains("  filesDir:", Config());                                         // the old key stays until someone removes it
        Assert.DoesNotContain("attachmentsDir", Config());
    }

    // ── the settings page: one line for plans, one for images ──

    [Fact(DisplayName = "The project card has a Plans line and an Images line, each with a path and Choose folder, and no one-folder option")]
    public async Task TwoLinesNoOneFolder()
    {
        Legacy();
        await using BunitContext ctx = NewContext(picker: _ => null);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();

        string[] kinds = [.. page.FindAll("[data-project] [data-folder]").Select(r => r.GetAttribute("data-folder")!)];
        Assert.Equal(["plans", "images"], kinds);
        Assert.Equal(["Plans", "Images"], page.FindAll("[data-project] [data-folder] th").Select(l => l.TextContent.Trim()));
        Assert.Equal((OldPlans, OldImages), (Field(page, "plans").GetAttribute("value"), Field(page, "images").GetAttribute("value")));
        Assert.Equal(2, page.FindAll("[data-project] button.pick-folder").Count);   // Choose folder on each
        Assert.All(page.FindAll("[data-project] button.pick-folder"), b => Assert.Equal("Choose folder", b.TextContent.Trim()));
        Assert.Empty(page.FindAll("input[aria-label='Files folder']"));
        Assert.Empty(page.FindAll(".files-legacy, .files-now, button.files-put-both"));
        Assert.DoesNotContain("one folder", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Actions(page, "plans"));                                             // Move and Point appear once the path differs

        await Type(page, "plans", "docs/plans");
        page.Eventually(() => Assert.Equal(["Move files here", "Point here"], Actions(page, "plans")));
        page.Eventually(() => Assert.Empty(Actions(page, "images")));
    }

    [Fact(DisplayName = "Typing a path for plans and one for images each saves on its own with Point here, and a refused folder says why")]
    public async Task EachPointsOnItsOwn()
    {
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();
        Assert.Equal("DevResources/plans", Field(page, "plans").GetAttribute("placeholder"));
        Assert.Equal(OldImages, Field(page, "images").GetAttribute("placeholder"));

        await Type(page, "plans", "docs/plans");
        await Point(page, "plans");

        page.Eventually(() => Assert.Equal(("docs/plans", OldImages), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root))));   // the images line did not move
        page.Eventually(() => Assert.Contains("plansDir: docs/plans", Config()));
        page.Eventually(() => Assert.DoesNotContain("attachmentsDir", Config()));
        page.Eventually(() => Assert.Equal("docs/plans", Field(page, "plans").GetAttribute("value")));
        page.Eventually(() => Assert.Empty(Actions(page, "plans")));

        await Type(page, "images", "pics");
        await Point(page, "images");

        page.Eventually(() => Assert.Equal(("docs/plans", "pics"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root))));
        page.Eventually(() => Assert.Contains("plansDir: docs/plans", Config()));
        page.Eventually(() => Assert.Contains("attachmentsDir: pics", Config()));

        await Type(page, "plans", "openspec/plans");                                      // a refused folder says why and changes nothing
        await Point(page, "plans");
        page.Eventually(() => Assert.Contains("openspec", page.Find("[data-folder='plans'] .att-warn").TextContent));
        page.Eventually(() => Assert.Equal("docs/plans", settings.PlansDir(repo.Root)));
        page.Eventually(() => Assert.Empty(page.FindAll("[data-folder='images'] .att-warn")));
    }

    [Fact(DisplayName = "Move files here on the Plans line takes the plans to the new folder and leaves the images, and the other way round")]
    public async Task MoveTakesOnlyItsOwnFiles()
    {
        Legacy();
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();

        await Type(page, "plans", "attachments/plans");
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf($"{OldPlans}/export-2026-11-09.md"))));       // nothing is saved or moved before the click
        page.Eventually(() => Assert.Equal(OldPlans, settings.PlansDir(repo.Root)));
        await Move(page, "plans");

        page.Eventually(() => Assert.Equal("attachments/plans", settings.PlansDir(repo.Root)));
        page.Eventually(() => Assert.Contains("Moved 3 files", page.Find("[data-folder='plans'] .att-done").TextContent));   // two plans and the drawing beside them
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf("attachments/plans/export-2026-11-09.md"))));
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf("attachments/plans/drawing.excalidraw.svg"))));
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf("attachments/plans/old-2026-09-01.md"))));
        page.Eventually(() => Assert.False(Directory.Exists(repo.PathOf(OldPlans))));
        page.Eventually(() => Assert.Equal(OldImages, settings.AttachmentsDir(repo.Root)));                       // the images are where they were
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf($"{OldImages}/wi123/n123/shot.png"))));
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf($"{OldImages}/wi123/pasted-1.png"))));
        page.Eventually(() => Assert.DoesNotContain("attachmentsDir", Config()));

        await Type(page, "images", "attachments/images");
        await Move(page, "images");

        page.Eventually(() => Assert.Equal("attachments/images", settings.AttachmentsDir(repo.Root)));
        page.Eventually(() => Assert.Contains("Moved 2 files", page.Find("[data-folder='images'] .att-done").TextContent));
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf("attachments/images/wi123/n123/shot.png"))));
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf("attachments/images/wi123/pasted-1.png"))));
        page.Eventually(() => Assert.False(Directory.Exists(repo.PathOf(OldImages))));
        page.Eventually(() => Assert.Equal("attachments/plans", settings.PlansDir(repo.Root)));                   // and the plans stayed where they went
    }

    [Fact(DisplayName = "After the moves, notes show their images, work items open their plans, and doc links in notes still resolve")]
    public async Task EverythingStillResolves()
    {
        Legacy();
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();
        await Type(page, "plans", "attachments/plans");
        await Move(page, "plans");
        await Type(page, "images", "attachments/images");
        await Move(page, "images");

        page.Eventually(() => Assert.Equal("attachments/plans/export-2026-11-09.md", WorkItemsApi.Get(123)!.Plan));        // the open item
        page.Eventually(() => Assert.Equal("attachments/plans/old-2026-09-01.md", WorkItemsApi.Get(124)!.Plan));           // and the archived one
        string note = Unix(repo.Read(NoteFile));
        page.Eventually(() => Assert.Contains("- attachments/images/wi123/n123/shot.png", note));
        page.Eventually(() => Assert.Contains("[[attachments/plans/export-2026-11-09]]", note));
        page.Eventually(() => Assert.Contains("(attachments/plans/drawing.excalidraw.svg)", note));
        string proposal = Unix(repo.Read(Proposal));
        page.Eventually(() => Assert.Contains("![](attachments/images/wi123/pasted-1.png)", proposal));
        page.Eventually(() => Assert.Contains("[[attachments/plans/export-2026-11-09.md]]", proposal));
        page.Eventually(() => Assert.DoesNotContain(OldPlans, note + proposal));
        page.Eventually(() => Assert.DoesNotContain(OldImages, note + proposal));
        page.Eventually(() => Assert.All(NotesApi.ResolveLinks("[[attachments/plans/export-2026-11-09]] [[attachments/plans/export-2026-11-09.md]]"), l => Assert.True(l.Resolved)));

        IRenderedComponent<WorkItemPage> item = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        item.Eventually(() => Assert.Equal(2, item.FindAll("img").Count(i => (i.GetAttribute("src") ?? "").StartsWith("data:image/png"))));   // the note's image and the description's
        await LinkedView(item);
        item.Eventually(() => Assert.Equal("doc/attachments/plans/export-2026-11-09.md", item.Find(".related li.rel[data-kind='plan'] a.rel-open").GetAttribute("href")));   // the plan link points at the new place

        IRenderedComponent<DocPage> doc = ctx.Render<DocPage>(p => p.Add(c => c.Path, "attachments/plans/export-2026-11-09.md"));
        item.Eventually(() => Assert.Contains("Export plan", doc.Markup));                                       // the plan opens...
        item.Eventually(() => Assert.Contains(doc.FindAll("img"), i => (i.GetAttribute("src") ?? "").StartsWith("data:image/svg")));   // ...with the drawing beside it
    }

    [Fact(DisplayName = "Point here only changes where new images and new plans go, and every old file and link stays")]
    public async Task PointOnlyChangesNewFiles()
    {
        Legacy();
        string note = repo.Read(NoteFile), proposal = repo.Read(Proposal), item = repo.Read(Item123);
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>().OnProject();

        await Type(page, "plans", "attachments/plans");
        await Point(page, "plans");
        await Type(page, "images", "attachments/images");
        await Point(page, "images");

        page.Eventually(() => Assert.Equal(("attachments/plans", "attachments/images"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root))));
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf($"{OldImages}/wi123/n123/shot.png"))));       // the old files stay...
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf($"{OldPlans}/export-2026-11-09.md"))));
        page.Eventually(() => Assert.Equal((note, proposal, item), (repo.Read(NoteFile), repo.Read(Proposal), repo.Read(Item123))));   // ...and every link to them is as it was
        page.Eventually(() => Assert.Equal($"{OldPlans}/export-2026-11-09.md", WorkItemsApi.Get(123)!.Plan));
        page.Eventually(() => Assert.True(NotesApi.AttachBytes(123, "new.png", Png).Success));                   // new images go to the new folder
        page.Eventually(() => Assert.True(File.Exists(repo.PathOf("attachments/images/wi123/n123/new.png"))));
        IRenderedComponent<DocPage> doc = ctx.Render<DocPage>(p => p.Add(c => c.Path, $"{OldPlans}/export-2026-11-09.md"));
        page.Eventually(() => Assert.Contains("Export plan", doc.Markup));                                       // the old link still opens
    }

    // ── moving plans ──

    [Fact(DisplayName = "A plan is moved with the drawings beside it, and the links inside the plan are left as they are")]
    public void PlansMoveWithTheirDrawings()
    {
        Legacy();
        OpenSpecStoreApi.Open(repo.Root);
        string planText = repo.Read($"{OldPlans}/export-2026-11-09.md");

        AttachmentsMoveResult result = NotesApi.MovePlans(repo.Root, OldPlans, "attachments/plans");

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, result.Moved);
        Assert.Equal(planText, repo.Read("attachments/plans/export-2026-11-09.md"));      // the relative drawing and plan links inside it are untouched
        Assert.Equal(Svg, repo.Read("attachments/plans/drawing.excalidraw.svg"));          // because the drawing moved beside it
        Assert.False(File.Exists(repo.PathOf($"{OldPlans}/export-2026-11-09.md")));
        Assert.False(File.Exists(repo.PathOf(".gitignore")) && repo.Read(".gitignore").Contains("attachments/plans"));   // plans are tracked: never ignored
        Assert.Equal("attachments/plans/export-2026-11-09.md", WorkItemsApi.Get(123)!.Plan);

        AttachmentsMoveResult again = NotesApi.MovePlans(repo.Root, OldPlans, "attachments/plans");   // safe to repeat
        Assert.True(again.Success);
        Assert.Equal(0, again.Moved);
        Assert.Equal(planText, repo.Read("attachments/plans/export-2026-11-09.md"));

        // a folder on the way that is a link is not followed: nothing is moved
        string elsewhere = Path.Combine(temp, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Junction(repo.PathOf("linked"), elsewhere);
        repo.Write($"{OldPlans}/again-2026-11-14.md", "# Again\n");
        AttachmentsMoveResult refused = NotesApi.MovePlans(repo.Root, OldPlans, "linked/plans");
        Assert.False(refused.Success);
        Assert.Contains("link", refused.Message);
        Assert.True(File.Exists(repo.PathOf($"{OldPlans}/again-2026-11-14.md")));
        Assert.Empty(Directory.GetFileSystemEntries(elsewhere));
    }

    [Fact(DisplayName = "Moving plans never overwrites: an identical file is dropped and a different one is kept beside it")]
    public void PlansNeverOverwritten()
    {
        repo.Write($"{OldPlans}/a-2026-11-01.md", "# From the old folder\n");
        repo.Write("attachments/plans/a-2026-11-01.md", "# Already there\n");
        repo.Write($"{OldPlans}/b-2026-11-02.md", "# Same\n");
        repo.Write("attachments/plans/b-2026-11-02.md", "# Same\n");
        repo.Write(Item123, Unix(repo.Read(Item123)).Replace("  title: Health-check endpoint\n", $"  title: Health-check endpoint\n  plan: {OldPlans}/a-2026-11-01.md\n"));
        OpenSpecStoreApi.Open(repo.Root);

        AttachmentsMoveResult result = NotesApi.MovePlans(repo.Root, OldPlans, "attachments/plans");

        Assert.True(result.Success, result.Message);
        Assert.Equal("# Already there\n", Unix(repo.Read("attachments/plans/a-2026-11-01.md")));      // never overwritten
        Assert.Equal("# From the old folder\n", Unix(repo.Read("attachments/plans/a-2026-11-01-2.md")));   // kept beside it
        Assert.Equal("# Same\n", Unix(repo.Read("attachments/plans/b-2026-11-02.md")));
        Assert.False(File.Exists(repo.PathOf("attachments/plans/b-2026-11-02-2.md")));      // the same bytes are not copied twice
        Assert.False(File.Exists(repo.PathOf($"{OldPlans}/b-2026-11-02.md")));              // the source is dropped
        Assert.Equal((2, 1, 1), (result.Moved + result.Duplicates, result.Renamed, result.Duplicates));
        Assert.Equal("attachments/plans/a-2026-11-01-2.md", WorkItemsApi.Get(123)!.Plan);    // the link follows the file to the name it got
    }

    // ── the callers ──

    [Fact(DisplayName = "The agent guide names the plans and images folders after a change, from Settings or from outside")]
    public async Task GuideNamesTheFolders()
    {
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        string guide = repo.PathOf("openspec/REPO-TASKS.md");
        Assert.Contains($"plans={OldPlans} attachments={OldImages}", File.ReadLines(guide).First());

        Assert.True(settings.SetPlansDir(repo.Root, "attachments/plans").Success);
        Assert.True(settings.SetAttachmentsDir(repo.Root, "attachments/images").Success);

        string text = File.ReadAllText(guide);
        Assert.Contains("plans=attachments/plans attachments=attachments/images", File.ReadLines(guide).First());
        Assert.Contains("`attachments/plans/<title-slug>-<yyyy-MM-dd>.md`", text);
        Assert.Contains("`attachments/images/wi<item>/n<note>/`", text);
        Assert.DoesNotContain(OldPlans, text);

        // the folders changed from outside (a git pull): the guide and the Notes module follow, the old one-folder key included
        repo.Write("openspec/config.yaml", "schema: spec-driven\n\nx-rtm:\n  plansDir: docs/plans\n  attachmentsDir: media/pics\n");
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        workspace.Notify(); workspace.Resolved.Wait();   // the scan and the guides check run after Changed, on the pool
        Assert.Contains("plans=docs/plans attachments=media/pics", File.ReadLines(guide).First());
        Assert.Equal(Path.GetFullPath(repo.PathOf("media/pics")), NotesApi.AttachmentsRoot(repo.Root));
        OneFolder("media/files", ctx);
        repo.Write("openspec/config.yaml", "schema: spec-driven\n\nx-rtm:\n  filesDir: media/files\n");
        OpenSpecStoreApi.Refresh();
        workspace.Notify(); workspace.Resolved.Wait();
        Assert.Contains("plans=media/files/plans attachments=media/files/images", File.ReadLines(guide).First());
        Assert.Equal(Path.GetFullPath(repo.PathOf("media/files/images")), NotesApi.AttachmentsRoot(repo.Root));
    }

    [Fact(DisplayName = "The board tools use the project's plans folder")]
    public async Task BoardToolsUseThePlansFolder()
    {
        WorkItemsApi.ConfigureValidator(folder => (true, $"valid: {folder}"));
        OpenSpecStoreApi.Open(repo.Root);   // the app has the project open; a tool call works in it without opening anything itself
        ServiceProvider services = new ServiceCollection().AddLogging().AddMcpServer().AddBoardTools().Services.BuildServiceProvider();
        System.IO.Pipelines.Pipe toServer = new(), toClient = new();
        McpServer server = McpServer.Create(
            new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()),
            services.GetRequiredService<IOptions<McpServerOptions>>().Value,
            services.GetRequiredService<ILoggerFactory>(), services);
        _ = server.RunAsync();
        McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
        try
        {
            async Task<string[]> Warnings(string plan)
            {
                CallToolResult r = await client.CallToolAsync("board_update", new Dictionary<string, object?> { ["id"] = 123, ["plan"] = plan, ["repo"] = repo.Root });
                Assert.NotEqual(true, r.IsError);
                return [.. r.StructuredContent!.Value.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()!)];
            }

            repo.Write("attachments/plans/in-2026-11-13.md", "# In\n");
            repo.Write($"{OldPlans}/out-2026-11-13.md", "# Out\n");
            Assert.Empty(await Warnings($"{OldPlans}/out-2026-11-13.md"));                // a project without a files folder: the old folder is the plans folder, no remark

            OneFolder("attachments");                                                      // the old one-folder key still names the plans folder
            Assert.Empty(await Warnings("attachments/plans/in-2026-11-13.md"));           // inside the project's plans folder: fine
            Assert.Equal("attachments/plans/in-2026-11-13.md", WorkItemsApi.Get(123)!.Plan);
            string[] elsewhere = await Warnings($"{OldPlans}/out-2026-11-13.md");          // the old place: linked, with a word about where plans go
            Assert.Contains(elsewhere, w => w.Contains("attachments/plans"));
            Assert.Equal($"{OldPlans}/out-2026-11-13.md", WorkItemsApi.Get(123)!.Plan);
            Assert.Contains("attachments/plans", NotesApi.ProjectFilesDir(repo.Root) is string d ? d + "/plans" : "");

            Assert.True(NewSettings().SetPlansDir(repo.Root, "docs/plans").Success);       // the plans folder of its own wins over the old key
            repo.Write("docs/plans/own-2026-11-18.md", "# Own\n");
            Assert.Empty(await Warnings("docs/plans/own-2026-11-18.md"));
            Assert.Contains(await Warnings("attachments/plans/in-2026-11-13.md"), w => w.Contains("docs/plans"));
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
            await services.DisposeAsync();
            WorkItemsApi.ConfigureValidator(null);
        }
    }

    [Fact(DisplayName = "The sync uses the images folder, and a files folder changed on another computer is followed")]
    public async Task SyncFollowsTheImagesFolder()
    {
        SettingsStore app = new(Path.Combine(temp, "app.json"));
        string storage = Path.Combine(temp, "storage");
        OpenSpecStoreApi.Open(repo.Root, keepOthers: true);
        OneFolder("attachments");
        NotesApi.SetAttachmentsDir(repo.Root, app.AttachmentsDir(repo.Root));            // what the workspace does for an open project
        List<SyncOptions> seen = [];
        UiService ui = new() { ToastLifetime = Timeout.InfiniteTimeSpan };   // a toast the test asserts on must not expire on the wall clock of a busy machine
        BoardStorageSettings settings = new(Path.Combine(temp, "data"), StorageProviders.Builtin);
        BoardSyncService sync = new(settings, ui, new SyncSetup(TimeSpan.FromMinutes(2), async o => { seen.Add(o); return await BoardSyncApi.Sync(o); }), app);
        disposables.Add(sync);
        sync.InProject = (root, work) => { OpenSpecStoreApi.Use(root); GitHistoryApi.Configure(root); work(); };
        _ = sync.Attach(repo.Root);
        Assert.True(settings.Connect(repo.Root, "folder").Success);
        Assert.True(settings.SetValue(repo.Root, "path", storage).Success);
        await sync.LastPass(repo.Root);
        Bytes("attachments/images/wi1/n1/pic.png");
        repo.Write("attachments/plans/p-2026-11-13.md", "# Plan\n");

        await sync.SyncNow(repo.Root);

        Assert.Equal(Path.GetFullPath(repo.PathOf("attachments/images")), seen.Last(o => o.WholeFilesOnly).LocalRoot);   // the images folder is what is synced, not the files folder
        string manifest = File.ReadAllText(Path.Combine(storage, "attachments", "manifest.json")).Replace("\\\\", "/");
        Assert.Contains("\"wi1/n1/pic.png\"", manifest);                                   // paths in the storage are relative to the images folder, so earlier content keeps matching
        Assert.DoesNotContain("images/wi1", manifest);
        Assert.DoesNotContain("p-2026-11-13", manifest);                                   // plans go through git

        // another computer moved the files folder; a pull brought the new config.yaml here
        repo.Write("openspec/config.yaml", Config().Replace("filesDir: attachments", "filesDir: media"));   // the connected storage stays in it
        OpenSpecStoreApi.Refresh();   // an outside edit, noticed by the board's file watcher
        sync.BoardChanged();
        await sync.QuietElapsed(repo.Root);

        Assert.Contains(ui.Toasts.Select(t => t.Text), t => t.StartsWith("Attachments moved to media/images"));
        Assert.True(File.Exists(repo.PathOf("media/images/wi1/n1/pic.png")));            // this computer's image files followed
        Assert.False(File.Exists(repo.PathOf("attachments/images/wi1/n1/pic.png")));
        Assert.True(File.Exists(repo.PathOf("attachments/plans/p-2026-11-13.md")));      // plans arrive through git: the app does not move them
        Assert.False(File.Exists(repo.PathOf("media/plans/p-2026-11-13.md")));
    }
}
