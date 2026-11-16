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
/// A project keeps its files in one folder: images (what notes and descriptions attach) and plans inside it, saved with the project (openspec/config.yaml, x-rtm.filesDir).
/// A project without one keeps its old layout. Nothing here waits for time: every step is a call or a click that is awaited.
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

    private BunitContext NewContext(SettingsStore? settings = null, bool storage = false, TestRepo? second = null)
    {
        BunitContext ctx = new();
        disposables.Add(ctx);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        UiService ui = new();
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

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<SettingsPage> page) => page.Find("section[data-project] input[aria-label='Files folder']");

    private static Task Type(IRenderedComponent<SettingsPage> page, string value) => page.InvokeAsync(() => Field(page).ChangeAsync(new ChangeEventArgs { Value = value }));

    private static Task Click(IRenderedComponent<SettingsPage> page, string selector) => page.InvokeAsync(() => page.Find(selector).ClickAsync(new MouseEventArgs()));

    /// <summary>The item page's "linked" view, where the plan and its link picker are.</summary>
    private static Task LinkedView(IRenderedComponent<WorkItemPage> page) => page.InvokeAsync(() => page.Find(".item-views input[value='linked']").ChangeAsync(new ChangeEventArgs { Value = "linked" }));

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

    [Fact(DisplayName = "A project's folders and its board storage are one group with no line between them")]
    public async Task OneGroupNoLine()
    {
        TestRepo second = new();
        others.Add(second);
        await using BunitContext ctx = NewContext(storage: true, second: second);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        IReadOnlyList<AngleSharp.Dom.IElement> storage = page.FindAll("section[data-storage]");
        Assert.Equal(2, storage.Count);                                                   // every project listed
        foreach (AngleSharp.Dom.IElement section in storage)
        {
            Assert.Contains("set-sub", section.ClassList);                                // part of the project's group
            Assert.Equal("section", section.PreviousElementSibling!.TagName.ToLowerInvariant());
            Assert.Equal(section.GetAttribute("data-project"), section.PreviousElementSibling!.GetAttribute("data-project"));   // right after its own project's block
            Assert.Equal("Board storage", section.QuerySelector("h3")!.TextContent.Trim());
        }
        Assert.Equal(2, page.FindAll("section[data-project]:not([data-storage]) h3").Count);   // the project's name is written once per project
        Assert.All(page.FindAll("section.set-row:not([data-storage])"), s => Assert.DoesNotContain("set-sub", s.ClassList));   // the lines between unrelated sections stay

        string css = Unix(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")));
        Assert.Contains("border-top: 2px dashed var(--line-soft)", css[css.IndexOf("  .set-row {", StringComparison.Ordinal)..].Split('\n')[0]);   // the line itself is unchanged
        Assert.Matches(@"\.set-row\.set-sub\s*\{[^}]*border-top:\s*0", css);               // and the group takes none of it
    }

    // ── the model: one files folder, two fixed parts ──

    [Fact(DisplayName = "A project with a files folder keeps images in its images folder and plans in its plans folder")]
    public async Task ImagesAndPlansInside()
    {
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        Assert.True(settings.SetFilesDir(repo.Root, "attachments").Success);

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
        await item.InvokeAsync(() => item.Find(".related button[aria-label='Link a plan']").ClickAsync(new MouseEventArgs()));
        string[] offered = [.. item.FindAll(".menu-pop .pick-item").Select(e => e.TextContent.Trim())];
        Assert.Contains("new-2026-11-13.md", offered);                                    // the picker lists the project's plans folder...
        Assert.DoesNotContain("elsewhere-2026-11-13.md", offered);                        // ...and no other
    }

    [Fact(DisplayName = "Only the images folder is kept out of git; plans stay tracked")]
    public async Task OnlyImagesIgnored()
    {
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        Assert.True(settings.SetFilesDir(repo.Root, "attachments").Success);

        Assert.True(NotesApi.AttachBytes(123, "x.png", Png).Success);

        string[] lines = [.. Unix(repo.Read(".gitignore")).Split('\n').Select(l => l.Trim())];
        Assert.Contains("attachments/images/", lines);
        Assert.DoesNotContain("attachments/", lines);                                     // the whole files folder is not ignored
        Assert.DoesNotContain("attachments", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("attachments/plans"));
        Assert.DoesNotContain(lines, l => l == "local/" || l == "local");                  // and the old folder is not added by the new layout
    }

    [Fact(DisplayName = "The files folder is saved with the project, and a project without one keeps its old plans and attachments folders")]
    public void SavedWithTheProject()
    {
        SettingsStore settings = NewSettings();
        string key = Path.GetFullPath(repo.Root).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        settings.Update(s => s with { PlansDirs = new() { [key] = "mine/plans" }, AttachmentsDirs = new() { [key] = "mine/pics" } });

        // no filesDir: the old layout, exactly as before; nothing is written or moved by itself
        Assert.Null(settings.FilesDir(repo.Root));
        Assert.Equal("mine/plans", settings.PlansDir(repo.Root));
        Assert.Equal("mine/pics", settings.AttachmentsDir(repo.Root));
        Assert.DoesNotContain("filesDir", Config());

        Assert.True(settings.SetFilesDir(repo.Root, "files\\all/").Success);              // tidied like the other folders

        Assert.Equal("files/all", settings.FilesDir(repo.Root));
        Assert.Equal(("files/all/plans", "files/all/images"), (settings.PlansDir(repo.Root), settings.AttachmentsDir(repo.Root)));
        string config = Config();
        Assert.Contains("x-rtm:\n  lanes:\n    In Progress: [High priority, Lower priority]\n  filesDir: files/all\n", config);   // one inline line in the project's block
        Assert.Contains("schema: spec-driven\n\n# lanes per column, written by the app\n", config);
        Assert.Null(settings.Current.PlansDirs);                                          // nothing writes the per-user values any more: this repository's are cleared
        Assert.Null(settings.Current.AttachmentsDirs);
        SettingsStore other = new(SettingsFile + ".other");                               // another computer reading the same repository gets the same folders
        Assert.Equal(("files/all/plans", "files/all/images"), (other.PlansDir(repo.Root), other.AttachmentsDir(repo.Root)));

        Assert.True(settings.SetFilesDir(repo.Root, "attachments").Success);              // the default is written too: its absence would mean the old layout
        Assert.Contains("  filesDir: attachments\n", Config());
        Assert.Equal("attachments/images", settings.AttachmentsDir(repo.Root));

        string before = Config();
        foreach (string bad in new[] { "openspec", "openspec/files", "../outside", "a/../../outside", repo.Root, "C:/elsewhere", "/abs", ".", "./", "a/<b>" })
        {
            (bool ok, string message) = settings.SetFilesDir(repo.Root, bad);
            Assert.False(ok, bad);
            Assert.NotEmpty(message);
        }
        Assert.Equal(before, Config());                                                   // refused: nothing changed
        Assert.Equal("attachments", settings.FilesDir(repo.Root));
    }

    // ── the settings page: choosing the folder ──

    [Fact(DisplayName = "Choosing a files folder when nothing exists yet saves it at once")]
    public async Task SavesAtOnce()
    {
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Equal("attachments", Field(page).GetAttribute("placeholder"));
        Assert.Equal("Images: local/task-tracking · Plans: DevResources/plans", page.Find("section[data-project] .files-now").TextContent.Trim());   // where things are now

        await Type(page, "attachments");

        Assert.Empty(page.FindAll(".att-ask"));
        Assert.Equal("attachments", settings.FilesDir(repo.Root));
        Assert.Contains("filesDir: attachments", Config());
        Assert.Equal("Images: attachments/images · Plans: attachments/plans", page.Find("section[data-project] .files-now").TextContent.Trim());
        Assert.Empty(page.FindAll(".files-legacy"));                                      // the offer to put both in one folder is for the old layout only

        await Type(page, "openspec/pics");                                                // a refused folder says why and changes nothing
        Assert.Contains("openspec", page.Find(".att-warn").TextContent);
        Assert.Equal("attachments", settings.FilesDir(repo.Root));
    }

    [Fact(DisplayName = "Choosing a files folder asks before moving, and Move takes images and plans to the new places")]
    public async Task AsksThenMoves()
    {
        Legacy();
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        await Type(page, "attachments");

        string ask = page.Find(".att-ask").TextContent;
        Assert.Contains($"Move 2 images from {OldImages} to attachments/images and 2 plans from {OldPlans} to attachments/plans?", ask);
        Assert.Contains("Notes, descriptions and plan links will point to the new places.", ask);
        Assert.Equal(["Move", "Keep them where they are", "Cancel"], page.FindAll(".att-ask button").Select(b => b.TextContent.Trim()));
        Assert.Null(settings.FilesDir(repo.Root));                                        // nothing is saved or moved before the answer
        Assert.True(File.Exists(repo.PathOf($"{OldImages}/wi123/n123/shot.png")));
        Assert.True(File.Exists(repo.PathOf($"{OldPlans}/export-2026-11-09.md")));

        await Click(page, ".att-ask .att-cancel");                                        // cancelling changes nothing and puts the field back
        Assert.Empty(page.FindAll(".att-ask"));
        Assert.Null(settings.FilesDir(repo.Root));
        Assert.True(string.IsNullOrEmpty(Field(page).GetAttribute("value")));

        await Type(page, "attachments");
        await Click(page, ".att-ask .att-move");

        Assert.Empty(page.FindAll(".att-ask"));
        Assert.Equal("attachments", settings.FilesDir(repo.Root));
        Assert.Contains("Moved", page.Find(".att-done").TextContent);
        Assert.True(File.Exists(repo.PathOf("attachments/images/wi123/n123/shot.png")));
        Assert.True(File.Exists(repo.PathOf("attachments/images/wi123/pasted-1.png")));
        Assert.True(File.Exists(repo.PathOf("attachments/plans/export-2026-11-09.md")));
        Assert.True(File.Exists(repo.PathOf("attachments/plans/drawing.excalidraw.svg")));
        Assert.True(File.Exists(repo.PathOf("attachments/plans/old-2026-09-01.md")));
        Assert.False(Directory.Exists(repo.PathOf(OldImages)));
        Assert.False(Directory.Exists(repo.PathOf(OldPlans)));
    }

    [Fact(DisplayName = "After the move, notes show their images, work items open their plans, and doc links in notes still resolve")]
    public async Task EverythingStillResolves()
    {
        Legacy();
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        await Type(page, "attachments");
        await Click(page, ".att-ask .att-move");

        Assert.Equal("attachments/plans/export-2026-11-09.md", WorkItemsApi.Get(123)!.Plan);        // the open item
        Assert.Equal("attachments/plans/old-2026-09-01.md", WorkItemsApi.Get(124)!.Plan);           // and the archived one
        string note = Unix(repo.Read(NoteFile));
        Assert.Contains("- attachments/images/wi123/n123/shot.png", note);
        Assert.Contains("[[attachments/plans/export-2026-11-09]]", note);
        Assert.Contains("(attachments/plans/drawing.excalidraw.svg)", note);
        string proposal = Unix(repo.Read(Proposal));
        Assert.Contains("![](attachments/images/wi123/pasted-1.png)", proposal);
        Assert.Contains("[[attachments/plans/export-2026-11-09.md]]", proposal);
        Assert.DoesNotContain(OldPlans, note + proposal);
        Assert.DoesNotContain(OldImages, note + proposal);
        Assert.All(NotesApi.ResolveLinks("[[attachments/plans/export-2026-11-09]] [[attachments/plans/export-2026-11-09.md]]"), l => Assert.True(l.Resolved));

        IRenderedComponent<WorkItemPage> item = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal(2, item.FindAll("img").Count(i => (i.GetAttribute("src") ?? "").StartsWith("data:image/png")));   // the note's image and the description's
        await LinkedView(item);
        Assert.Equal("doc/attachments/plans/export-2026-11-09.md", item.Find(".related li.rel[data-kind='plan'] a.rel-open").GetAttribute("href"));   // the plan link points at the new place

        IRenderedComponent<DocPage> doc = ctx.Render<DocPage>(p => p.Add(c => c.Path, "attachments/plans/export-2026-11-09.md"));
        Assert.Contains("Export plan", doc.Markup);                                       // the plan opens...
        Assert.Contains(doc.FindAll("img"), i => (i.GetAttribute("src") ?? "").StartsWith("data:image/svg"));   // ...with the drawing beside it
    }

    [Fact(DisplayName = "Keep them where they are only changes where new images and new plans go")]
    public async Task KeepOnlyChangesNewFiles()
    {
        Legacy();
        string note = repo.Read(NoteFile), proposal = repo.Read(Proposal), item = repo.Read(Item123);
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        await Type(page, "attachments");
        await Click(page, ".att-ask .att-keep");

        Assert.Equal("attachments", settings.FilesDir(repo.Root));
        Assert.Empty(page.FindAll(".att-ask"));
        Assert.True(File.Exists(repo.PathOf($"{OldImages}/wi123/n123/shot.png")));       // the old files stay...
        Assert.True(File.Exists(repo.PathOf($"{OldPlans}/export-2026-11-09.md")));
        Assert.Equal((note, proposal, item), (repo.Read(NoteFile), repo.Read(Proposal), repo.Read(Item123)));   // ...and every link to them is as it was
        Assert.Equal($"{OldPlans}/export-2026-11-09.md", WorkItemsApi.Get(123)!.Plan);
        Assert.True(NotesApi.AttachBytes(123, "new.png", Png).Success);                   // new images go to the new folder
        Assert.True(File.Exists(repo.PathOf("attachments/images/wi123/n123/new.png")));
        Assert.Equal("attachments/plans", settings.PlansDir(repo.Root));                  // and new plans are offered from the new one
        IRenderedComponent<DocPage> doc = ctx.Render<DocPage>(p => p.Add(c => c.Path, $"{OldPlans}/export-2026-11-09.md"));
        Assert.Contains("Export plan", doc.Markup);                                       // the old link still opens
    }

    // ── moving plans ──

    [Fact(DisplayName = "A plan is moved with the drawings beside it, and the links inside the plan are left as they are")]
    public void PlansMoveWithTheirDrawings()
    {
        Legacy();
        OpenSpecStoreApi.Open(repo.Root);
        string planText = repo.Read($"{OldPlans}/export-2026-11-09.md");
        Assert.Equal((2, 3), NotesApi.CountPlanFiles(repo.Root, OldPlans));               // two plans, three files with the drawing

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

    [Fact(DisplayName = "The agent guide names the new plans and images folders after the change")]
    public async Task GuideNamesTheFolders()
    {
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        Workspace workspace = ctx.Services.GetRequiredService<Workspace>();
        string guide = repo.PathOf("openspec/REPO-TASKS.md");
        Assert.Contains($"plans={OldPlans} attachments={OldImages}", File.ReadLines(guide).First());

        Assert.True(settings.SetFilesDir(repo.Root, "attachments").Success);

        string text = File.ReadAllText(guide);
        Assert.Contains("plans=attachments/plans attachments=attachments/images", File.ReadLines(guide).First());
        Assert.Contains("`attachments/plans/<title-slug>-<yyyy-MM-dd>.md`", text);
        Assert.Contains("`attachments/images/wi<item>/n<note>/`", text);
        Assert.DoesNotContain(OldPlans, text);

        // the folder changed from outside (a git pull): the guide and the Notes module follow
        repo.Write("openspec/config.yaml", "schema: spec-driven\n\nx-rtm:\n  filesDir: media/files\n");
        workspace.Notify();
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

            Assert.True(NewSettings().SetFilesDir(repo.Root, "attachments").Success);
            Assert.Empty(await Warnings("attachments/plans/in-2026-11-13.md"));           // inside the project's plans folder: fine
            Assert.Equal("attachments/plans/in-2026-11-13.md", WorkItemsApi.Get(123)!.Plan);
            string[] elsewhere = await Warnings($"{OldPlans}/out-2026-11-13.md");          // the old place: linked, with a word about where plans go
            Assert.Contains(elsewhere, w => w.Contains("attachments/plans"));
            Assert.Equal($"{OldPlans}/out-2026-11-13.md", WorkItemsApi.Get(123)!.Plan);
            Assert.Contains("attachments/plans", NotesApi.ProjectFilesDir(repo.Root) is string d ? d + "/plans" : "");
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
        Assert.True(app.SetFilesDir(repo.Root, "attachments").Success);
        NotesApi.SetAttachmentsDir(repo.Root, app.AttachmentsDir(repo.Root));            // what the workspace does for an open project
        List<SyncOptions> seen = [];
        UiService ui = new();
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
        sync.BoardChanged();
        await sync.QuietElapsed(repo.Root);

        Assert.Contains(ui.Toasts.Select(t => t.Text), t => t.StartsWith("Attachments moved to media/images"));
        Assert.True(File.Exists(repo.PathOf("media/images/wi1/n1/pic.png")));            // this computer's image files followed
        Assert.False(File.Exists(repo.PathOf("attachments/images/wi1/n1/pic.png")));
        Assert.True(File.Exists(repo.PathOf("attachments/plans/p-2026-11-13.md")));      // plans arrive through git: the app does not move them
        Assert.False(File.Exists(repo.PathOf("media/plans/p-2026-11-13.md")));
    }

    [Fact(DisplayName = "Put both in one folder fills the field and asks the same question")]
    public async Task PutBothInOneFolder()
    {
        Legacy();
        SettingsStore settings = NewSettings();
        await using BunitContext ctx = NewContext(settings);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.True(string.IsNullOrEmpty(Field(page).GetAttribute("value")));
        Assert.Equal($"Plans are in {OldPlans} and images in {OldImages}. Put both in one folder", page.Find("section[data-project] .files-legacy").TextContent.Trim());
        await Type(page, "attachments");
        string typedAsk = page.Find(".att-ask p").TextContent;
        await Click(page, ".att-ask .att-cancel");
        Assert.Empty(page.FindAll(".att-ask"));

        await Click(page, "button.files-put-both");

        Assert.Equal("attachments", Field(page).GetAttribute("value"));
        Assert.Equal(typedAsk, page.Find(".att-ask p").TextContent);
        Assert.Equal(["Move", "Keep them where they are", "Cancel"], page.FindAll(".att-ask button").Select(b => b.TextContent.Trim()));
        Assert.Null(settings.FilesDir(repo.Root));                                        // still a question: nothing is saved yet
    }
}
