using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using GitHistory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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

namespace TaskManager.Tests;

/// <summary>
/// #wiN, #nN and [[doc]] mean the same thing wherever they are written: one grammar in OpenSpecStore finds them, renames them, parses a typed one and
/// recognises a mention of one, and the note page, the worklog and a document all show the same links for the same text.
/// </summary>
public sealed class LinkingReferencesEverywhereTests : IDisposable
{
    private const string Text = "See #wi123, #n123 and [[runbook/uptime]]; not #wi9999 or `#wi123` in code.";
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-refs-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        return ctx;
    }

    private static IReadOnlyList<(string Tag, string Class, string Href, string Text)> Links(IElement within) =>
        [.. within.QuerySelectorAll(".wl").Select(e => (e.TagName.ToLowerInvariant(), e.GetAttribute("class") ?? "", e.GetAttribute("href") ?? "", e.TextContent))];

    private static readonly (string, string, string, string)[] Expected =
    [
        ("a", "wl", "item/123", "#wi123"),
        ("a", "wl", "note/123", "#n123"),
        ("span", "wl", "", "runbook/uptime"),
        ("span", "wl bad", "", "#wi9999"),
    ];

    // ── the grammar ──

    [Fact(DisplayName = "Writing #wi12, #n3 and [[guide]] in a text finds each reference once, with where it stands")]
    public void FindsEachReference()
    {
        string text = "Fix #wi12 (see #n3) and [[ guide ]].";

        IReadOnlyList<OpenSpecStore.PublicDTOs.Reference> found = OpenSpecStoreApi.FindReferences(text);

        Assert.Equal([("item", "12", 12), ("note", "3", 3), ("doc", "guide", 0)], found.Select(r => (r.Kind, r.Target, r.Number)));
        Assert.Equal(["#wi12", "#n3", "[[ guide ]]"], found.Select(r => text.Substring(r.Start, r.Length)));
    }

    [Fact(DisplayName = "#wi123 is not #wi1234, #wi12a or text glued to a word, and a number too long for an id is no reference")]
    public void OneBoundaryRule()
    {
        Assert.Empty(OpenSpecStoreApi.FindReferences("#wi1234abc #n5x foo#wi9 ##wi8 #wi12_ #wi1234567890"));
        Assert.Equal(["12", "13", "14", "15"], OpenSpecStoreApi.FindReferences("(#wi12) #wi13, #wi14. #wi15-x").Select(r => r.Target));
    }

    [Fact(DisplayName = "Renumbering an item rewrites #wi12 but not #wi123")]
    public void RenameKeepsLongerNumbers()
    {
        string renamed = OpenSpecStoreApi.RenameReference("#wi12 and #wi123, #wi12. [[12]] #n12", "item", 12, 40);

        Assert.Equal("#wi40 and #wi123, #wi40. [[12]] #n12", renamed);
        Assert.Equal("#n40 but #n4", OpenSpecStoreApi.RenameReference("#n12 but #n4", "note", 12, 40));
    }

    [Fact(DisplayName = "Typing #wi12, wi12 or 12 in the subtask box names item 12, and other text names none")]
    public void TypedReferences()
    {
        Assert.Equal([12, 12, 12, null, null], new[] { "#wi12", "wi12", "12", "wi12 x", "#wi" }.Select(s => OpenSpecStoreApi.ParseItemReference(s, bareNumber: true)));
        Assert.Equal([12, 12, null], new[] { "#wi12", "wi12", "12" }.Select(s => OpenSpecStoreApi.ParseItemReference(s)));
    }

    [Fact(DisplayName = "A reference is written back the way it is read")]
    public void FormatsWhatItFinds()
    {
        Assert.Equal("#wi12", OpenSpecStoreApi.FormatReference("item", "12"));
        Assert.Equal("#n3", OpenSpecStoreApi.FormatReference("note", "3"));
        Assert.Equal("[[guide]]", OpenSpecStoreApi.FormatReference("doc", "guide"));
    }

    // ── one rendering ──

    [Fact(DisplayName = "A note that mentions #wi12, #n3 and [[guide]] shows the same links on the note page, in the worklog and in a document")]
    public async Task SameLinksOnEveryPage()
    {
        repo.Write("openspec/changes/add-health-check/notes/n123.md", $"---\nid: 123\nby: Artyom\ncreated: 2026-10-02T09:14:00\n---\n# Ping test\n\n{Text}\n");
        repo.Write("docs/runbook/uptime.md", "# Uptime\n");
        repo.Write("docs/same.md", Text + "\n");
        await using BunitContext ctx = NewContext();

        IRenderedComponent<NotePage> notePage = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        IRenderedComponent<Worklog> worklog = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));
        IRenderedComponent<DocPage> doc = ctx.Render<DocPage>(p => p.Add(c => c.Path, "docs/same.md"));

        Assert.Equal(Expected, Links(notePage.Find(".note-view")));
        Assert.Equal(Expected, Links(worklog.Find("article[data-note='123'] .wk-body")));
        Assert.Equal(Expected, Links(doc.Find(".desc")));
        Assert.Contains("#wi123", doc.Find(".desc code").TextContent);   // in code it stays as written
    }

    [Fact(DisplayName = "A [[doc]] that is not in the repository is struck through the same way on every page")]
    public async Task MissingDocIsBad()
    {
        repo.Write("docs/same.md", "Read [[nothing/here]].\n");
        await using BunitContext ctx = NewContext();

        IRenderedComponent<DocPage> doc = ctx.Render<DocPage>(p => p.Add(c => c.Path, "docs/same.md"));

        Assert.Equal([("span", "wl bad", "", "nothing/here")], Links(doc.Find(".desc")));
    }

    // ── an agent's note ──

    [Fact(DisplayName = "An agent's board_note mentioning #wi123 links the same way as one written in the app")]
    public async Task AgentNoteLinksLikeAppNote()
    {
        repo.Write("docs/runbook/uptime.md", "# Uptime\n");
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureValidator(folder => (true, "valid"));
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddMcpServer().AddBoardTools().Services.BuildServiceProvider();
        Pipe toServer = new(), toClient = new();
        await using McpServer server = McpServer.Create(
            new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()),
            services.GetRequiredService<IOptions<McpServerOptions>>().Value, services.GetRequiredService<ILoggerFactory>(), services);
        _ = server.RunAsync();
        await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
        try
        {
            CallToolResult made = await client.CallToolAsync("board_note", new Dictionary<string, object?> { ["repo"] = repo.Root, ["id"] = 123, ["title"] = "Agent", ["body"] = Text, ["agent"] = "Codex" });
            Assert.NotEqual(true, made.IsError);
            int agentNote = made.StructuredContent!.Value.GetProperty("noteId").GetInt32();
            int appNote = NotesApi.Create(123, "Artyom").Id;
            Assert.True(NotesApi.SaveBody(appNote, Text).Success);
            await using BunitContext ctx = NewContext();

            IRenderedComponent<Worklog> worklog = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));

            Assert.Equal(Expected, Links(worklog.Find($"article[data-note='{agentNote}'] .wk-body")));
            Assert.Equal(Expected, Links(worklog.Find($"article[data-note='{appNote}'] .wk-body")));
        }
        finally { WorkItemsApi.ConfigureValidator(null); }
    }

    // ── a commit message ──

    [Fact(DisplayName = "A commit message mentioning #wi123 links to the item, and #wi1234 does not")]
    public void CommitMentionsLinkToTheItem()
    {
        Git("init", "-q");
        Git("config", "user.name", "Artyom Test");
        Git("config", "user.email", "artyom@example.com");
        Git("config", "commit.gpgsign", "false");
        File.WriteAllText(repo.PathOf("a.txt"), "1");
        Git("add", "-A");
        Git("commit", "-q", "-m", "Add the route (#wi123)");
        File.WriteAllText(repo.PathOf("b.txt"), "2");
        Git("add", "-A");
        Git("commit", "-q", "-m", "Fix #wi1234");
        GitHistoryApi.Configure(repo.Root);

        Assert.Equal(["Add the route (#wi123)"], GitHistoryApi.Mentioning(OpenSpecStoreApi.MentionsItem(123)).Select(c => c.Subject));
        Assert.Equal(["Fix #wi1234"], GitHistoryApi.Mentioning(OpenSpecStoreApi.MentionsItem(1234)).Select(c => c.Subject));
    }

    private string Git(params string[] args)
    {
        ProcessStartInfo psi = new("git") { WorkingDirectory = repo.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using Process p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }
}
