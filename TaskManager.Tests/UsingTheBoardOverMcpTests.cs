using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenSpecStore;
using TaskManager.Mcp;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>The eight board tools, called the way an agent calls them: through an MCP client over an in-memory pipe.</summary>
[Collection(nameof(ModuleStateCollection))]
public sealed class UsingTheBoardOverMcpTests : IAsyncLifetime
{
    private ServiceProvider services = null!;
    private McpServer server = null!;
    private McpClient client = null!;
    private readonly TestRepo repo = new();

    public async Task InitializeAsync()
    {
        WorkItemsApi.ConfigureValidator(folder => (true, $"valid: {folder}"));
        services = new ServiceCollection().AddLogging().AddMcpServer().AddBoardTools().Services.BuildServiceProvider();
        Pipe toServer = new(), toClient = new();
        server = McpServer.Create(
            new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()),
            services.GetRequiredService<IOptions<McpServerOptions>>().Value,
            services.GetRequiredService<ILoggerFactory>(), services);
        _ = server.RunAsync();
        client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
    }

    public async Task DisposeAsync()
    {
        await client.DisposeAsync();
        await server.DisposeAsync();
        await services.DisposeAsync();
        WorkItemsApi.ConfigureValidator(null);
        repo.Dispose();
    }

    private async Task<(bool IsError, JsonElement Data, string Text)> Call(string tool, Dictionary<string, object?> args)
    {
        args["repo"] = repo.Root;
        CallToolResult result = await client.CallToolAsync(tool, args);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        JsonElement data = result.StructuredContent is { } s ? s.Clone() : default;
        return (result.IsError == true, data, text);
    }

    private async Task<JsonElement> Ok(string tool, Dictionary<string, object?> args)
    {
        (bool isError, JsonElement data, string text) = await Call(tool, args);
        Assert.False(isError, text);
        return data;
    }

    private static string[] Strings(JsonElement list) => list.EnumerateArray().Select(e => e.GetString()!).ToArray();

    private const string Why = "Agents need the same board the user sees, with the same stamps, so that nothing they do goes unreviewed.";

    [Fact(DisplayName = "The server lists the eight board tools, each with a description")]
    public async Task ListsTools()
    {
        IList<McpClientTool> tools = await client.ListToolsAsync();

        Assert.Equal(
            ["board_create", "board_features", "board_get", "board_list", "board_log", "board_note", "board_update", "board_validate"],
            tools.Select(t => t.Name).Order());
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Name));
        Assert.Equal("repo-tasks", client.ServerInfo.Name);
        Assert.Contains("In Review", client.ServerInstructions);
        Assert.True(client.ServerInstructions!.Length < 4000);
    }

    [Fact(DisplayName = "board_create makes a valid change folder with the next id, the agent in contributors and the agent stamp, and returns the validator's output")]
    public async Task Create()
    {
        OpenSpecStoreApi.Open(repo.Root);
        int expected = WorkItemsApi.NextId();

        JsonElement r = await Ok("board_create", new()
        {
            ["title"] = "Rate-limit the API",
            ["why"] = Why,
            ["whatChanges"] = "- Each key gets 100 requests a minute.",
            ["feature"] = "api-limits",
            ["label"] = "feature",
            ["status"] = "Planned",
            ["agent"] = "Codex",
            ["subtasks"] = new[] { new { group = "Limiter", text = "Count requests per key" }, new { group = "Limiter", text = "Refuse the 101st" }, new { group = "Docs", text = "Describe the limit" } },
            ["specs"] = new Dictionary<string, string> { ["api-limits"] = "## ADDED Requirements\n\n### Requirement: Limit\nThe API SHALL refuse a key's 101st request in a minute.\n\n#### Scenario: Limit\n- **WHEN** a key sends its 101st request\n- **THEN** the response is 429\n" },
        });

        int id = r.GetProperty("id").GetInt32();
        Assert.Equal(expected, id);
        Assert.Equal("rate-limit-the-api", r.GetProperty("folder").GetString());
        Assert.True(r.GetProperty("valid").GetBoolean());
        Assert.Equal("valid: rate-limit-the-api", r.GetProperty("validation").GetString());

        string yaml = repo.Read("openspec/changes/rate-limit-the-api/.openspec.yaml");
        Assert.Contains($"id: {id}", yaml);
        Assert.Contains("contributors: [Codex]", yaml);
        Assert.Contains("agentBy: Codex", yaml);
        Assert.Matches(@"agentAt: \d{4}-\d\d-\d\dT\d\d:\d\d:\d\d", yaml);
        Assert.Contains("label: feature", yaml);
        Assert.DoesNotContain("skip_specs", yaml);
        string proposal = repo.Read("openspec/changes/rate-limit-the-api/proposal.md");
        Assert.Contains("## Why\n\n" + Why, proposal.Replace("\r\n", "\n"));
        Assert.Contains("- Each key gets 100 requests a minute.", proposal);
        string tasks = repo.Read("openspec/changes/rate-limit-the-api/tasks.md").Replace("\r\n", "\n");
        Assert.Contains("## 1. Limiter\n\n- [ ] 1.1 Count requests per key <!-- agent: Codex -->\n- [ ] 1.2 Refuse the 101st <!-- agent: Codex -->", tasks);
        Assert.Contains("## 2. Docs\n\n- [ ] 2.1 Describe the limit <!-- agent: Codex -->", tasks);
        Assert.True(File.Exists(repo.PathOf("openspec/changes/rate-limit-the-api/specs/api-limits/spec.md")));
        Assert.Equal(3, WorkItemsApi.Get(id)!.Subtasks.Count);
    }

    [Fact(DisplayName = "board_update changes status, priority, label, plan and subtasks, signs new subtasks and refuses Completed with a message about archiving from the app")]
    public async Task Update()
    {
        await Ok("board_update", new()
        {
            ["id"] = 107, ["status"] = "In Progress", ["priority"] = "high", ["label"] = "bug", ["plan"] = "DevResources/plans/export-2026-11-09.md",
            ["agent"] = "Codex",
            ["subtasks"] = new object[] { new { text = "Write the CSV" }, new { number = "1.1", done = true } },
        });

        WorkItems.PublicDTOs.WorkItem item = WorkItemsApi.Get(107)!;
        Assert.Equal(("In Progress", "high", "bug", "DevResources/plans/export-2026-11-09.md"), (item.Status, item.Priority, item.Label, item.Plan));
        Assert.Equal("Codex", item.AgentBy);
        Assert.Contains("Codex", item.Contributors);
        Assert.NotNull(item.Started);
        Assert.Contains(item.Subtasks, s => s.Text == "Write the CSV" && s.Agent == "Codex" && !s.Done);
        Assert.Contains("<!-- agent: Codex -->", repo.Read("openspec/changes/export-remittances/tasks.md"));

        (bool isError, JsonElement _, string text) = await Call("board_update", new() { ["id"] = 107, ["status"] = "Completed" });
        Assert.True(isError);
        Assert.Contains("archives from the app", text);
        Assert.Contains("In Review", text);
        Assert.Equal("In Progress", WorkItemsApi.Get(107)!.Status);

        (bool unknownError, JsonElement _, string unknown) = await Call("board_update", new() { ["id"] = 99999, ["status"] = "Planned" });
        Assert.True(unknownError);
        Assert.Contains("99999", unknown);

        await Ok("board_update", new() { ["id"] = 107, ["status"] = "In Review" });
        Assert.Equal("In Review", WorkItemsApi.Get(107)!.Status);
    }

    [Fact(DisplayName = "board_note adds a note with the agent as author and the agent stamp, and board_get lists it")]
    public async Task Note()
    {
        JsonElement made = await Ok("board_note", new()
        {
            ["id"] = 107, ["title"] = "9 Nov — export wired", ["body"] = "The CSV export writes one row per remittance.",
            ["links"] = new[] { "wi123" }, ["attachments"] = new[] { "https://example.com/spec" }, ["agent"] = "Codex",
        });

        int noteId = made.GetProperty("noteId").GetInt32();
        string note = repo.Read($"openspec/changes/export-remittances/notes/n{noteId}.md");
        Assert.Contains("by: Codex", note);
        Assert.Contains("agentBy: Codex", note);
        Assert.Contains("agentAt: ", note);
        Assert.Contains("title: 9 Nov", note);
        Assert.Contains("links:", note);
        Assert.Equal(["wi123"], Notes.NotesApi.Get(noteId)!.ManualLinks);
        Assert.Contains("https://example.com/spec", note);
        Assert.Contains("The CSV export writes one row per remittance.", note);
        Assert.Equal("Codex", WorkItemsApi.Get(107)!.AgentBy);

        JsonElement concise = await Ok("board_get", new() { ["id"] = 107 });
        JsonElement row = concise.GetProperty("notes").EnumerateArray().Single(n => n.GetProperty("id").GetInt32() == noteId);
        Assert.Equal(("9 Nov — export wired", "Codex"), (row.GetProperty("title").GetString(), row.GetProperty("by").GetString()));
        Assert.False(row.TryGetProperty("body", out JsonElement b) && b.ValueKind == JsonValueKind.String);

        JsonElement detailed = await Ok("board_get", new() { ["id"] = 107, ["format"] = "detailed" });
        Assert.Contains("one row per remittance", detailed.GetProperty("notes").EnumerateArray().Single(n => n.GetProperty("id").GetInt32() == noteId).GetProperty("body").GetString());
        Assert.Equal(107, detailed.GetProperty("id").GetInt32());
        Assert.False(string.IsNullOrEmpty(detailed.GetProperty("revision").GetString()));
    }

    [Fact(DisplayName = "board_list filters by status and feature and pages with after and limit")]
    public async Task List()
    {
        JsonElement all = await Ok("board_list", new());
        int[] ids = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToArray();
        Assert.Contains(123, ids);
        Assert.Contains(107, ids);
        Assert.Equal(ids.Order(), ids);

        JsonElement planned = await Ok("board_list", new() { ["status"] = new[] { "Planned" } });
        Assert.All(planned.GetProperty("items").EnumerateArray(), i => Assert.Equal("Planned", i.GetProperty("status").GetString()));
        Assert.Contains(planned.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetInt32() == 107);

        JsonElement security = await Ok("board_list", new() { ["feature"] = "non-functional/security" });
        Assert.Equal([123], security.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()));

        JsonElement first = await Ok("board_list", new() { ["limit"] = 1 });
        Assert.Equal([ids[0]], first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()));
        int next = first.GetProperty("nextAfter").GetInt32();
        Assert.Equal(ids[0], next);
        JsonElement second = await Ok("board_list", new() { ["limit"] = 1, ["after"] = next });
        Assert.Equal([ids[1]], second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()));

        JsonElement ready = await Ok("board_list", new() { ["ready"] = true });
        Assert.DoesNotContain(123, ready.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()));   // already In Progress
        Assert.Contains(107, ready.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()));

        JsonElement concise = all.GetProperty("items")[0];
        Assert.True(concise.TryGetProperty("revision", out _));
        Assert.True(concise.TryGetProperty("title", out _));
    }

    [Fact(DisplayName = "board_update reports test names that are not in the latest results")]
    public async Task UnknownTests()
    {
        repo.Write("results/run.trx", ReadingLinkedTestRunsTests.Trx);
        ReadingLinkedTestRunsTests.UseResults(repo, "results/*.trx");

        JsonElement r = await Ok("board_update", new()
        {
            ["id"] = 107,
            ["tests"] = new object[] { new { suite = "UptimeTests", test = "Ping test" }, new { suite = "UptimeTests", test = "Not a real test" } },
        });

        string[] warnings = Strings(r.GetProperty("warnings"));
        Assert.Single(warnings);
        Assert.Contains("Not a real test", warnings[0]);
        string yaml = repo.Read("openspec/changes/export-remittances/.openspec.yaml");
        Assert.Contains("Ping test", yaml);
        Assert.Contains("Not a real test", yaml);   // still written

        JsonElement got = await Ok("board_get", new() { ["id"] = 107 });
        JsonElement[] tests = got.GetProperty("tests").EnumerateArray().ToArray();
        Assert.Equal("passed", tests.Single(t => t.GetProperty("test").GetString() == "Ping test").GetProperty("outcome").GetString());
        Assert.Equal("missing", tests.Single(t => t.GetProperty("test").GetString() == "Not a real test").GetProperty("outcome").GetString());
    }

    [Fact(DisplayName = "A tool call outside any repository fails with a message naming the path it tried")]
    public async Task OutsideRepository()
    {
        string outside = Path.Combine(Path.GetTempPath(), "rtm-no-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            CallToolResult result = await client.CallToolAsync("board_list", new Dictionary<string, object?> { ["repo"] = outside });

            Assert.True(result.IsError);
            string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            Assert.Contains(outside, text);
            Assert.Contains("openspec", text);
        }
        finally { Directory.Delete(outside); }
    }

    [Fact(DisplayName = "board_features lists capabilities with their open items, and board_validate runs the validator and reports the app's warnings")]
    public async Task FeaturesAndValidate()
    {
        JsonElement features = await Ok("board_features", new());
        JsonElement security = features.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("path").GetString() == "non-functional/security");
        Assert.Equal([123], security.GetProperty("openItems").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()));

        JsonElement one = await Ok("board_validate", new() { ["id"] = 107, ["format"] = "detailed" });
        Assert.True(one.GetProperty("allValid").GetBoolean());
        Assert.Equal("valid: export-remittances", one.GetProperty("items")[0].GetProperty("output").GetString());

        JsonElement every = await Ok("board_validate", new());
        Assert.True(every.GetProperty("items").GetArrayLength() >= 2);
        Assert.Equal(JsonValueKind.Array, every.GetProperty("warnings").ValueKind);
    }

    [Fact(DisplayName = "board_update and board_note refuse a stale revision with the current one in the message, and accept the one board_get returned")]
    public async Task StaleRevision()
    {
        string read = (await Ok("board_get", new() { ["id"] = 123 })).GetProperty("revision").GetString()!;
        await Ok("board_update", new() { ["id"] = 123, ["title"] = "Edited by someone else" });
        string now = (await Ok("board_get", new() { ["id"] = 123 })).GetProperty("revision").GetString()!;
        Assert.Matches("^[0-9a-f]{12}$", now);
        Assert.NotEqual(read, now);

        (bool refused, _, string text) = await Call("board_update", new() { ["id"] = 123, ["revision"] = read, ["title"] = "Overwritten" });
        Assert.True(refused);
        Assert.Contains(now, text);
        Assert.DoesNotContain("Overwritten", repo.Read("openspec/changes/add-health-check/.openspec.yaml"));

        (bool noteRefused, _, string noteText) = await Call("board_note", new() { ["id"] = 123, ["revision"] = read, ["title"] = "Late", ["body"] = "Written against a stale read." });
        Assert.True(noteRefused);
        Assert.Contains(now, noteText);

        JsonElement ok = await Ok("board_update", new() { ["id"] = 123, ["revision"] = now, ["title"] = "Health check, renamed" });
        Assert.NotEqual(now, ok.GetProperty("revision").GetString());
    }

    [Fact(DisplayName = "Agents creating work items at the same moment get different ids")]
    public async Task ConcurrentCreates()
    {
        Task<JsonElement>[] calls = [.. Enumerable.Range(0, 6).Select(n => Ok("board_create", new() { ["title"] = $"Parallel {n}", ["why"] = Why, ["whatChanges"] = "- One thing.", ["agent"] = n % 2 == 0 ? "Claude" : "Codex" }))];

        int[] ids = [.. (await Task.WhenAll(calls)).Select(r => r.GetProperty("id").GetInt32())];

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact(DisplayName = "What an agent changes through the tools is recorded in the item's worklog under the agent's name")]
    public async Task ToolsRecordEventsUnderTheAgent()
    {
        await Ok("board_update", new()
        {
            ["id"] = 107, ["status"] = "In Progress", ["commits"] = new[] { "abc1234" }, ["agent"] = "Codex",
            ["subtasks"] = new object[] { new { number = "1.2", done = true } },
        });

        IReadOnlyList<WorkItems.PublicDTOs.WorkItemEvent> events = WorkItemsApi.GetEvents(107);
        Assert.Contains(events, e => e.Kind == "status" && e.Text == "Planned -> In Progress");
        Assert.Contains(events, e => e.Kind == "commit-linked" && e.Text == "abc1234");
        Assert.Contains(events, e => e.Kind == "subtask-done" && e.Text.StartsWith("1.2 ", StringComparison.Ordinal));
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal("Codex", e.By));

        // the actor is the agent only while its tool runs: a direct command afterwards is somebody else's
        WorkItemsApi.SetSubtaskDone(107, "1.2", false);
        Assert.NotEqual("Codex", WorkItemsApi.GetEvents(107)[^1].By);
    }

    [Fact(DisplayName = "board_get returns the worklog, notes and events together, newest first, the newest twenty in concise format")]
    public async Task GetReturnsTheWorklog()
    {
        string log = string.Concat(Enumerable.Range(1, 24).Select(n => $"2020-01-01T10:{n:00}:00 | Codex | status | Step {n}\n"));
        repo.Write("openspec/changes/export-remittances/events.log", log);
        JsonElement made = await Ok("board_note", new() { ["id"] = 107, ["title"] = "Ran the export", ["body"] = "It wrote 12 rows.", ["agent"] = "Codex" });
        int noteId = made.GetProperty("noteId").GetInt32();

        JsonElement concise = await Ok("board_get", new() { ["id"] = 107 });
        JsonElement[] feed = [.. concise.GetProperty("worklog").EnumerateArray()];
        Assert.Equal(25, concise.GetProperty("worklogTotal").GetInt32());
        Assert.Equal(20, feed.Length);
        Assert.Equal(("note", "Ran the export", "Codex", noteId), (feed[0].GetProperty("kind").GetString(), feed[0].GetProperty("text").GetString(), feed[0].GetProperty("by").GetString(), feed[0].GetProperty("noteId").GetInt32()));
        Assert.False(feed[0].TryGetProperty("body", out JsonElement hidden) && hidden.ValueKind == JsonValueKind.String);
        Assert.Equal(("status", "Step 24"), (feed[1].GetProperty("kind").GetString(), feed[1].GetProperty("text").GetString()));
        Assert.Equal("Step 6", feed[^1].GetProperty("text").GetString());
        string[] times = [.. feed.Select(f => f.GetProperty("at").GetString()!)];
        Assert.Equal(times.OrderByDescending(t => t, StringComparer.Ordinal), times);
        Assert.Equal(1, concise.GetProperty("notes").GetArrayLength());   // the notes field stays

        JsonElement detailed = await Ok("board_get", new() { ["id"] = 107, ["format"] = "detailed" });
        JsonElement all = detailed.GetProperty("worklog");
        Assert.Equal(25, all.GetArrayLength());
        Assert.Contains("12 rows", all[0].GetProperty("body").GetString());
        Assert.Equal("Step 1", all[24].GetProperty("text").GetString());
    }

    [Fact(DisplayName = "board_update links documents, and board_note writes an entry whose title is its first line when none is given")]
    public async Task DocsAndUntitledNotes()
    {
        repo.Write("docs/guide.md", "# Guide\n");

        JsonElement r = await Ok("board_update", new() { ["id"] = 107, ["docs"] = new[] { "docs/guide.md", "docs/missing.md" }, ["agent"] = "Codex" });

        Assert.Equal(["docs/guide.md", "docs/missing.md"], WorkItemsApi.Get(107)!.Docs);
        string[] warnings = Strings(r.GetProperty("warnings"));
        Assert.Single(warnings);
        Assert.Contains("docs/missing.md", warnings[0]);
        Assert.Contains(WorkItemsApi.GetEvents(107), e => e.Kind == "doc-linked" && e.Text == "docs/guide.md" && e.By == "Codex");
        Assert.Equal(["docs/guide.md", "docs/missing.md"], Strings((await Ok("board_get", new() { ["id"] = 107 })).GetProperty("docs")));

        (bool refused, _, string text) = await Call("board_update", new() { ["id"] = 107, ["docs"] = new[] { "../outside.md" } });
        Assert.True(refused);
        Assert.Contains("inside the repository", text);

        JsonElement made = await Ok("board_note", new() { ["id"] = 107, ["body"] = "\n## Ran the migration on staging\n\n```\n12 rows\n```\n", ["agent"] = "Codex" });
        Assert.Equal("Ran the migration on staging", Notes.NotesApi.Get(made.GetProperty("noteId").GetInt32())!.Title);

        JsonElement longOne = await Ok("board_note", new() { ["id"] = 107, ["body"] = new string('x', 120) + "\nmore", ["agent"] = "Codex" });
        Assert.Equal(80, Notes.NotesApi.Get(longOne.GetProperty("noteId").GetInt32())!.Title.Length);

        // a plain first line becomes the title and is not said twice: the text under it is the rest
        JsonElement plain = await Ok("board_note", new() { ["id"] = 107, ["body"] = "Ran the tests against dev\n\n```\n8 of 8\n```", ["agent"] = "Codex" });
        Notes.PublicDTOs.Note written = Notes.NotesApi.Get(plain.GetProperty("noteId").GetInt32())!;
        Assert.Equal("Ran the tests against dev", written.Title);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(written.Body, "Ran the tests against dev"));
        Assert.Contains("8 of 8", written.Body);
    }

    [Fact(DisplayName = "board_note turns unticked checkboxes in its body into subtasks and reports their numbers")]
    public async Task NoteBecomesSubtasks()
    {
        JsonElement made = await Ok("board_note", new()
        {
            ["id"] = 123, ["title"] = "Plan", ["agent"] = "Codex",
            ["body"] = "What is left:\n\n- [ ] restrict permissions\n- [x] already done\n- [ ] write the docs",
        });

        Assert.Equal(["2.2", "2.3"], Strings(made.GetProperty("subtasksCreated")));
        Assert.Equal(["restrict permissions", "write the docs"], WorkItemsApi.Get(123)!.Subtasks.TakeLast(2).Select(s => s.Text));
        string body = Notes.NotesApi.Get(made.GetProperty("noteId").GetInt32())!.Body;
        Assert.Contains("- [ ] restrict permissions (created subtask)", body);
        Assert.Contains("- [x] already done", body);

        JsonElement plain = await Ok("board_note", new() { ["id"] = 123, ["title"] = "Nothing to promote", ["body"] = "Just words.", ["agent"] = "Codex" });
        Assert.Empty(Strings(plain.GetProperty("subtasksCreated")));
    }

    [Fact(DisplayName = "A tool call reads what is on disk now, without waiting for the file watcher")]
    public async Task FreshReads()
    {
        const string yaml = "openspec/changes/add-health-check/.openspec.yaml";
        Assert.Equal("In Progress", (await Ok("board_get", new() { ["id"] = 123 })).GetProperty("status").GetString());   // the server has the item cached now

        repo.Write(yaml, repo.Read(yaml).Replace("status: In Progress", "status: Blocked"));   // the app, in another process

        Assert.Equal("Blocked", (await Ok("board_get", new() { ["id"] = 123 })).GetProperty("status").GetString());
    }

    [Fact(DisplayName = "board_create and board_update with titles, features, plans, docs and test names full of YAML-significant characters write files that validate and read back identical")]
    public async Task YamlSignificantInputs()
    {
        OpenSpecStoreApi.Open(repo.Root);
        string[] agents = ["yes", "null", "123", "On", "1e5"];
        int n = 0;
        foreach (string v in YamlChecks.Nasty)
        {
            string because = $"(value: {v})", agent = agents[n++ % agents.Length];
            string path = v.Replace('\\', '/').Trim();
            bool pathy = !Path.IsPathRooted(path) && !path.StartsWith('/') && !path.Contains("..") && !(path.Length > 1 && path[1] == ':');

            JsonElement made = await Ok("board_create", new() { ["title"] = v, ["why"] = Why, ["whatChanges"] = "- One thing.", ["feature"] = v, ["agent"] = agent });
            int id = made.GetProperty("id").GetInt32();
            Dictionary<string, object?> update = new()
            {
                ["id"] = id, ["agent"] = agent, ["title"] = v, ["contributors"] = new[] { v, agent },
                ["tests"] = new[] { new { suite = v, test = v } }, ["commits"] = new[] { v },
            };
            if (pathy) { update["plan"] = v; update["docs"] = new[] { v, "docs/a.md" }; }
            await Ok("board_update", update);
            string url = "https://example.com/" + v.Replace('\n', ' ');
            string[] attachments = Uri.TryCreate(url.Trim(), UriKind.Absolute, out _) ? [url] : [];
            JsonElement note = await Ok("board_note", new() { ["id"] = id, ["body"] = "What happened.", ["title"] = v, ["links"] = new[] { v }, ["commits"] = new[] { v }, ["attachments"] = attachments, ["agent"] = agent });

            WorkItem it = WorkItemsApi.Get(id)!;
            YamlChecks.Valid(repo.Read(Path.GetRelativePath(repo.Root, Path.Combine(it.Path, ".openspec.yaml")).Replace('\\', '/')), YamlChecks.Nasty.Concat(agents), because);
            string noteFile = note.GetProperty("path").GetString()!;
            (string front, string _) = Notes.BusinessRules.NoteFile.Split(repo.Read(noteFile));
            YamlChecks.Valid(front[4..^5], YamlChecks.Nasty.Concat(agents), because);

            OpenSpecStoreApi.Open(repo.Root);   // from the files, not from the cache
            it = WorkItemsApi.Get(id)!;
            Assert.Equal(v.Trim(), it.Title);
            Assert.Equal(v, Assert.Single(it.Features).Path);
            Assert.Equal([v, agent], it.Assigned);
            Assert.Equal([new TestLink(v, v, null)], it.Tests);
            Assert.Equal([v], it.Commits);
            Assert.Equal(agent, it.AgentBy);
            if (pathy) { Assert.Equal(path, it.Plan); Assert.Equal([path, "docs/a.md"], it.Docs); }
            Assert.Null(it.Warning);
            Notes.PublicDTOs.Note written = Notes.NotesApi.Get(note.GetProperty("noteId").GetInt32())!;
            Assert.Equal(string.Join(' ', v.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)), written.Title);
            Assert.Equal(agent, written.By);
            Assert.Equal([v], written.Commits);
            Assert.Equal(v.Trim(), written.ManualLinks[0]);
            Assert.Equal(attachments.Select(a => a.Trim()), written.Attachments);
        }
    }

    // ── v15: tools only, log as you go, decisions ──

    [Fact(DisplayName = "board_note with kind decision writes a decision note titled Decision that board_get returns with its kind, and an unknown kind is refused")]
    public async Task NoteKindDecision()
    {
        JsonElement made = await Ok("board_note", new() { ["id"] = 107, ["kind"] = "decision", ["body"] = "Page the list with nextAfter.\nWhy: one rule for every client.", ["agent"] = "Codex" });

        int noteId = made.GetProperty("noteId").GetInt32();
        Assert.Equal("decision", made.GetProperty("kind").GetString());
        Notes.PublicDTOs.Note written = Notes.NotesApi.Get(noteId)!;
        Assert.Equal(("Decision", "decision", "Codex"), (written.Title, written.Kind, written.By));
        Assert.Contains("kind: decision", repo.Read($"openspec/changes/export-remittances/notes/n{noteId}.md"));
        Assert.Contains("Why: one rule for every client.", written.Body);

        JsonElement got = await Ok("board_get", new() { ["id"] = 107 });
        JsonElement row = got.GetProperty("notes").EnumerateArray().Single(n => n.GetProperty("id").GetInt32() == noteId);
        Assert.Equal(("Decision", "decision"), (row.GetProperty("title").GetString(), row.GetProperty("kind").GetString()));

        JsonElement plain = await Ok("board_note", new() { ["id"] = 107, ["body"] = "An ordinary entry.", ["agent"] = "Codex" });
        Assert.False(plain.TryGetProperty("kind", out JsonElement k) && k.ValueKind == JsonValueKind.String);
        Assert.Null(Notes.NotesApi.Get(plain.GetProperty("noteId").GetInt32())!.Kind);

        int before = Notes.NotesApi.GetForWorkItem(107).Count;
        (bool isError, JsonElement _, string text) = await Call("board_note", new() { ["id"] = 107, ["kind"] = "idea", ["body"] = "Nope." });
        Assert.True(isError);
        Assert.Contains("decision", text);
        Assert.Contains("idea", text);
        Assert.Equal(before, Notes.NotesApi.GetForWorkItem(107).Count);
    }

    [Fact(DisplayName = "board_list always returns nextAfter, null on the last page, so a client that enforces the output schema accepts it")]
    public async Task ListAlwaysReturnsNextAfter()
    {
        JsonElement last = await Ok("board_list", new());
        Assert.True(last.TryGetProperty("nextAfter", out JsonElement next), "nextAfter is missing from the last page");
        Assert.Equal(JsonValueKind.Null, next.ValueKind);

        string json = JsonSerializer.Serialize(new ListResult(0, 0, null, []), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"nextAfter\":null", json);

        McpClientTool tool = (await client.ListToolsAsync()).Single(t => t.Name == "board_list");
        JsonElement schema = tool.ProtocolTool.OutputSchema!.Value;
        bool required = schema.TryGetProperty("required", out JsonElement req) && req.EnumerateArray().Any(r => r.GetString() == "nextAfter");
        string type = schema.GetProperty("properties").GetProperty("nextAfter").GetProperty("type").ToString();
        Assert.True(!required || type.Contains("null"), $"nextAfter is required but its type {type} does not allow null");
    }

    [Fact(DisplayName = "The server instructions say tools only, log as you go, and decisions as decision notes")]
    public void InstructionsSayToolsOnlyLogAndDecisions()
    {
        string text = BoardMcp.Instructions;
        foreach (string s in new[] { "only way to touch the board", "never edit openspec/ by hand", "Log as you go", "supervisor and every subagent", "one short `board_note` per event",
                     "not one summary at the end", "`repo`", "`kind: \"decision\"`", "clear and brief", "what was decided", "rules out", "-> board_note (log as you go, kind decision) ->",
                     "board_get each in detailed format", "read its worklog", "added since you last looked", "say what is new before acting" })
            Assert.Contains(s, text);
        Assert.True(text.Length < 4000);
    }

    // ── events for every write ──

    [Fact(DisplayName = "board_create records a subtask-added event for every subtask it writes, and a board_update field change is recorded like the app's")]
    public async Task WritesAreRecordedAsEvents()
    {
        JsonElement r = await Ok("board_create", new()
        {
            ["title"] = "Rate-limit the API",
            ["why"] = Why,
            ["whatChanges"] = "- Each key gets 100 requests a minute.",
            ["agent"] = "Codex",
            ["subtasks"] = new[] { new { group = "Limiter", text = "Count requests per key" }, new { group = "Limiter", text = "Refuse the 101st" }, new { group = "Docs", text = "Describe the limit" } },
        });
        int id = r.GetProperty("id").GetInt32();

        WorkItemEvent[] added = [.. WorkItemsApi.GetEvents(id).Where(e => e.Kind == "subtask-added")];
        Assert.Equal(["1.1 Count requests per key", "1.2 Refuse the 101st", "2.1 Describe the limit"], added.Select(e => e.Text));
        Assert.All(added, e => Assert.Equal("Codex", e.By));

        await Ok("board_update", new() { ["id"] = id, ["title"] = "Rate limiting", ["priority"] = "high", ["agent"] = "Codex" });
        await Ok("board_update", new() { ["id"] = id, ["title"] = "Rate limiting", ["priority"] = "high", ["agent"] = "Codex" });   // the same values again

        (string Kind, string Text, string By)[] events = [.. WorkItemsApi.GetEvents(id).Select(e => (e.Kind, e.Text, e.By))];
        Assert.Single(events, e => e.Kind == "title" && e.Text == "Rate-limit the API -> Rate limiting" && e.By == "Codex");
        Assert.Single(events, e => e.Kind == "priority" && e.Text.EndsWith("-> high") && e.By == "Codex");
    }

    // ── results match their output schema ──

    [Fact(DisplayName = "Every tool's result matches its output schema: nullable fields such as due, plan and nextAfter are present as null, so a strict client accepts board_get and board_list")]
    public async Task EveryResultMatchesItsOutputSchema()
    {
        IList<McpClientTool> tools = await client.ListToolsAsync();
        List<string> problems = [];

        async Task Check(string tool, Dictionary<string, object?> args)
        {
            JsonElement data = await Ok(tool, args);
            JsonElement schema = tools.Single(t => t.Name == tool).ProtocolTool.OutputSchema!.Value;
            MissingRequired(schema, data, tool, problems);
        }

        await Check("board_get", new() { ["id"] = 107 });
        await Check("board_get", new() { ["id"] = 107, ["format"] = "detailed" });
        await Check("board_list", new());
        await Check("board_features", new());
        await Check("board_validate", new() { ["id"] = 107 });
        await Check("board_note", new() { ["id"] = 107, ["body"] = "An ordinary entry.", ["agent"] = "Codex" });
        await Check("board_update", new() { ["id"] = 107, ["priority"] = "low" });
        await Check("board_update", new() { ["id"] = 107, ["testsAdd"] = new[] { new { suite = "UptimeTests", test = "Ping test" } }, ["note"] = new { body = "Done in one call.", kind = "decision" }, ["agent"] = "Codex" });
        await Check("board_log", new() { ["id"] = 107, ["text"] = "ran the suite: 525 passed", ["agent"] = "Codex" });
        await Check("board_update", new() { ["id"] = 123, ["resolution"] = "cancelled", ["agent"] = "Codex" });
        await Check("board_create", new() { ["title"] = "Rate-limit the API", ["why"] = Why, ["whatChanges"] = "- Each key gets 100 requests a minute.", ["agent"] = "Codex" });

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>What a strict client checks: every property the schema lists as required is present in the JSON, and nested objects and array items likewise.</summary>
    private static void MissingRequired(JsonElement schema, JsonElement value, string path, List<string> problems)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;
        if (value.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out JsonElement props))
        {
            HashSet<string> required = schema.TryGetProperty("required", out JsonElement req) ? [.. req.EnumerateArray().Select(r => r.GetString()!)] : [];
            foreach (string name in required)
                if (!value.TryGetProperty(name, out _)) problems.Add($"{path}: required property '{name}' is missing");
            foreach (JsonProperty p in props.EnumerateObject())
                if (value.TryGetProperty(p.Name, out JsonElement child)) MissingRequired(p.Value, child, $"{path}.{p.Name}", problems);
        }
        else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out JsonElement items))
        {
            int i = 0;
            foreach (JsonElement e in value.EnumerateArray()) MissingRequired(items, e, $"{path}[{i++}]", problems);
        }
    }

    // ── partial reads ──

    [Fact(DisplayName = "board_get returns only the sections asked for, always with the header and revision, and refuses an unknown section naming the valid ones")]
    public async Task GetReturnsOnlyTheSectionsAskedFor()
    {
        await Ok("board_note", new() { ["id"] = 107, ["title"] = "Ran the export", ["body"] = "It wrote 12 rows.", ["agent"] = "Codex" });

        JsonElement subtasks = await Ok("board_get", new() { ["id"] = 107, ["sections"] = new[] { "subtasks" } });
        Assert.Equal(107, subtasks.GetProperty("id").GetInt32());
        Assert.False(string.IsNullOrEmpty(subtasks.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(subtasks.GetProperty("status").GetString()));
        Assert.False(string.IsNullOrEmpty(subtasks.GetProperty("revision").GetString()));
        Assert.Equal(2, subtasks.GetProperty("subtasks").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, subtasks.GetProperty("description").ValueKind);
        Assert.Equal(0, subtasks.GetProperty("notes").GetArrayLength());
        Assert.Equal(0, subtasks.GetProperty("worklog").GetArrayLength());
        Assert.Equal(0, subtasks.GetProperty("tests").GetArrayLength());
        Assert.Equal(0, subtasks.GetProperty("deltaSpecs").GetArrayLength());

        JsonElement specs = await Ok("board_get", new() { ["id"] = 107, ["sections"] = new[] { "specs" } });
        JsonElement delta = specs.GetProperty("deltaSpecs").EnumerateArray().Single();
        Assert.Equal("functional/remittances", delta.GetProperty("capability").GetString());
        Assert.Contains("### Requirement: Monthly export", delta.GetProperty("markdown").GetString());
        Assert.Equal(0, specs.GetProperty("subtasks").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, specs.GetProperty("description").ValueKind);

        JsonElement some = await Ok("board_get", new() { ["id"] = 107, ["sections"] = new[] { "Description", "notes", "header" } });
        Assert.Contains("monthly export", some.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, some.GetProperty("notes").GetArrayLength());
        Assert.Equal(0, some.GetProperty("subtasks").GetArrayLength());

        JsonElement header = await Ok("board_get", new() { ["id"] = 107, ["sections"] = new[] { "header" } });
        Assert.False(string.IsNullOrEmpty(header.GetProperty("revision").GetString()));
        Assert.Equal(0, header.GetProperty("worklog").GetArrayLength());

        JsonElement everything = await Ok("board_get", new() { ["id"] = 107 });
        Assert.Equal(2, everything.GetProperty("subtasks").GetArrayLength());
        Assert.Equal(JsonValueKind.String, everything.GetProperty("description").ValueKind);

        (bool isError, _, string text) = await Call("board_get", new() { ["id"] = 107, ["sections"] = new[] { "subtasks", "bogus" } });
        Assert.True(isError);
        Assert.Contains("bogus", text);
        foreach (string valid in new[] { "header", "description", "subtasks", "notes", "worklog", "specs", "tests", "links" }) Assert.Contains(valid, text);
    }

    [Fact(DisplayName = "board_get since and limit cut the worklog and notes to the newest entries from that moment, and worklogTotal still counts them all")]
    public async Task GetSinceAndLimitCutTheWorklog()
    {
        string log = string.Concat(Enumerable.Range(1, 24).Select(n => $"2020-01-01T10:{n:00}:00 | Codex | status | Step {n}\n"));
        repo.Write("openspec/changes/export-remittances/events.log", log);
        await Ok("board_note", new() { ["id"] = 107, ["title"] = "First note", ["body"] = "One.", ["agent"] = "Codex" });
        JsonElement second = await Ok("board_note", new() { ["id"] = 107, ["title"] = "Second note", ["body"] = "Two.", ["agent"] = "Codex" });
        string[] sections = ["worklog", "notes"];

        JsonElement since = await Ok("board_get", new() { ["id"] = 107, ["since"] = "2020-01-01T10:20:00", ["sections"] = sections });
        int total = since.GetProperty("worklogTotal").GetInt32();
        Assert.True(total >= 26, $"worklogTotal {total}");
        string[] texts = [.. since.GetProperty("worklog").EnumerateArray().Select(w => w.GetProperty("text").GetString()!)];
        Assert.Equal("Second note", texts[0]);
        Assert.Equal("First note", texts[1]);
        Assert.Equal(["Step 24", "Step 23", "Step 22", "Step 21", "Step 20"], texts.Where(t => t.StartsWith("Step")));
        Assert.DoesNotContain("Step 19", texts);
        Assert.Equal(2, since.GetProperty("notes").GetArrayLength());

        JsonElement limited = await Ok("board_get", new() { ["id"] = 107, ["since"] = "2020-01-01T10:20:00", ["limit"] = 3, ["sections"] = sections });
        string[] three = [.. limited.GetProperty("worklog").EnumerateArray().Select(w => w.GetProperty("text").GetString()!)];
        Assert.Equal(3, three.Length);
        Assert.Equal(texts[..3], three);
        Assert.Equal(total, limited.GetProperty("worklogTotal").GetInt32());

        JsonElement oneNote = await Ok("board_get", new() { ["id"] = 107, ["limit"] = 1, ["sections"] = new[] { "notes" } });
        Assert.Equal(second.GetProperty("noteId").GetInt32(), oneNote.GetProperty("notes").EnumerateArray().Single().GetProperty("id").GetInt32());

        string today = DateTime.Now.ToString("yyyy-MM-dd");
        JsonElement fromToday = await Ok("board_get", new() { ["id"] = 107, ["since"] = today, ["sections"] = sections });
        Assert.All(fromToday.GetProperty("worklog").EnumerateArray(), w => Assert.True(string.CompareOrdinal(w.GetProperty("at").GetString(), today) >= 0));
        Assert.DoesNotContain(fromToday.GetProperty("worklog").EnumerateArray(), w => w.GetProperty("text").GetString()!.StartsWith("Step"));
        Assert.Equal(2, fromToday.GetProperty("notes").GetArrayLength());

        JsonElement future = await Ok("board_get", new() { ["id"] = 107, ["since"] = "2999-01-01", ["sections"] = sections });
        Assert.Equal(0, future.GetProperty("worklog").GetArrayLength());
        Assert.Equal(0, future.GetProperty("notes").GetArrayLength());
        Assert.Equal(total, future.GetProperty("worklogTotal").GetInt32());

        (bool isError, _, string text) = await Call("board_get", new() { ["id"] = 107, ["since"] = "last tuesday" });
        Assert.True(isError);
        Assert.Contains("since", text);
    }

    [Fact(DisplayName = "board_get concise cuts a long description to its first lines and says how long it is, and counts the subtasks done")]
    public async Task GetConciseCutsTheDescription()
    {
        string body = "## Why\n\n" + string.Concat(Enumerable.Range(1, 100).Select(n => $"Line {n:000} of a long description.\n"));
        repo.Write("openspec/changes/export-remittances/proposal.md", "# Proposal\n\n" + body);

        JsonElement concise = await Ok("board_get", new() { ["id"] = 107 });
        string cut = concise.GetProperty("description").GetString()!;
        Assert.Equal(600, cut.Length);
        Assert.StartsWith("## Why", cut);
        Assert.True(concise.GetProperty("descriptionCut").GetBoolean());
        Assert.Equal(1, concise.GetProperty("subtasksDone").GetInt32());
        Assert.Equal(2, concise.GetProperty("subtasksTotal").GetInt32());
        Assert.Equal(2, concise.GetProperty("subtasks").GetArrayLength());

        JsonElement detailed = await Ok("board_get", new() { ["id"] = 107, ["format"] = "detailed" });
        string full = detailed.GetProperty("description").GetString()!;
        Assert.True(full.Length > 600);
        Assert.False(detailed.GetProperty("descriptionCut").GetBoolean());
        Assert.Equal(full.Length, detailed.GetProperty("descriptionLength").GetInt32());
        Assert.Equal(full.Length, concise.GetProperty("descriptionLength").GetInt32());

        repo.Write("openspec/changes/export-remittances/proposal.md", "# Proposal\n\n## Why\n\nShort.\n");
        JsonElement shortOne = await Ok("board_get", new() { ["id"] = 107 });
        Assert.False(shortOne.GetProperty("descriptionCut").GetBoolean());
        Assert.Equal(shortOne.GetProperty("descriptionLength").GetInt32(), shortOne.GetProperty("description").GetString()!.Length);
    }

    [Fact(DisplayName = "board_features returns a capability's requirement names, and one requirement's full text with its scenarios")]
    public async Task FeaturesReadsAMainSpec()
    {
        repo.Write("openspec/specs/functional/remittances/spec.md",
            "# Remittances Specification\n\n## Purpose\n\nSample.\n\n## Requirements\n\n### Requirement: Remittances exists\nThe system SHALL provide Remittances.\n\n#### Scenario: Basic use\n- **WHEN** a user uses Remittances\n- **THEN** it works\n\n### Requirement: Monthly export\nThe system SHALL export a month.\n\n#### Scenario: Works\n- **WHEN** the user exports\n- **THEN** a CSV is written\n\n#### Scenario: Empty month\n- **WHEN** there is nothing to export\n- **THEN** the file has a header only\n");

        JsonElement names = await Ok("board_features", new() { ["format"] = "detailed", ["feature"] = "functional/remittances" });
        Assert.Equal(["Remittances exists", "Monthly export"], Strings(names.GetProperty("requirements")));
        Assert.Equal("functional/remittances", names.GetProperty("features").EnumerateArray().Single().GetProperty("path").GetString());

        JsonElement one = await Ok("board_features", new() { ["format"] = "detailed", ["feature"] = "functional/remittances", ["requirement"] = "monthly export" });
        string text = one.GetProperty("requirementText").GetString()!.Replace("\r\n", "\n");
        Assert.StartsWith("### Requirement: Monthly export", text);
        Assert.Contains("The system SHALL export a month.", text);
        Assert.Contains("#### Scenario: Works", text);
        Assert.Contains("#### Scenario: Empty month", text);
        Assert.Contains("the file has a header only", text);
        Assert.DoesNotContain("Remittances exists", text);

        (bool noFormat, _, string t1) = await Call("board_features", new() { ["feature"] = "functional/remittances" });
        Assert.True(noFormat);
        Assert.Contains("detailed", t1);
        (bool noFeature, _, string t2) = await Call("board_features", new() { ["format"] = "detailed", ["feature"] = "nope/none" });
        Assert.True(noFeature);
        Assert.Contains("nope/none", t2);
        (bool noReq, _, string t3) = await Call("board_features", new() { ["format"] = "detailed", ["feature"] = "functional/remittances", ["requirement"] = "Nope" });
        Assert.True(noReq);
        Assert.Contains("Monthly export", t3);
        (bool onlyReq, _, _) = await Call("board_features", new() { ["format"] = "detailed", ["requirement"] = "Monthly export" });
        Assert.True(onlyReq);

        IList<McpClientTool> tools = await client.ListToolsAsync();
        List<string> problems = [];
        void Check(string tool, JsonElement data) => MissingRequired(tools.Single(t => t.Name == tool).ProtocolTool.OutputSchema!.Value, data, tool, problems);
        Check("board_features", names);
        Check("board_features", one);
        foreach (string[] sections in new[] { new[] { "links" }, ["header"], ["specs"], ["description", "tests"] })
            Check("board_get", await Ok("board_get", new() { ["id"] = 107, ["sections"] = sections, ["since"] = "2020-01-01", ["limit"] = 5 }));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }


    // ── hand-off in one call ──

    private static string StrictTrx(params (string Class, string Name)[] tests) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Results>"
        + string.Concat(tests.Select((t, i) => $"<UnitTestResult testId=\"{i}\" testName=\"{t.Name}\" outcome=\"Passed\" endTime=\"2026-10-17T14:02:00.0000000+00:00\" />"))
        + "</Results><TestDefinitions>"
        + string.Concat(tests.Select((t, i) => $"<UnitTest id=\"{i}\" name=\"{t.Name}\"><TestMethod className=\"Ns.{t.Class}\" name=\"M{i}\" /></UnitTest>"))
        + "</TestDefinitions></TestRun>";

    [Fact(DisplayName = "board_note returns the item's new revision, and board_update with a note writes it in the same call")]
    public async Task NoteReturnsRevisionAndUpdateWritesNote()
    {
        string before = (await Ok("board_get", new() { ["id"] = 107 })).GetProperty("revision").GetString()!;

        JsonElement note = await Ok("board_note", new() { ["id"] = 107, ["body"] = "Tests are red.", ["agent"] = "Codex" });

        string after = (await Ok("board_get", new() { ["id"] = 107 })).GetProperty("revision").GetString()!;
        Assert.NotEqual(before, after);
        Assert.Equal(after, note.GetProperty("revision").GetString());

        JsonElement r = await Ok("board_update", new()
        {
            ["id"] = 107, ["revision"] = after, ["priority"] = "high", ["agent"] = "Codex",
            ["note"] = new { title = "Chunk done", body = "Passed: 525", kind = "decision" },
        });

        int noteId = r.GetProperty("noteId").GetInt32();
        Notes.PublicDTOs.Note written = Notes.NotesApi.Get(noteId)!;
        Assert.Equal(("Chunk done", "Codex", "decision"), (written.Title, written.By, written.Kind));
        string file = repo.Read($"openspec/changes/export-remittances/notes/n{noteId}.md");
        Assert.Contains("agentBy: Codex", file);
        Assert.Contains("Passed: 525", file);
        Assert.Equal("high", WorkItemsApi.Get(107)!.Priority);
        Assert.Equal((await Ok("board_get", new() { ["id"] = 107 })).GetProperty("revision").GetString(), r.GetProperty("revision").GetString());

        JsonElement without = await Ok("board_update", new() { ["id"] = 107, ["priority"] = "low" });
        Assert.Equal(JsonValueKind.Null, without.GetProperty("noteId").ValueKind);

        (bool bad, JsonElement _, string text) = await Call("board_update", new() { ["id"] = 107, ["priority"] = "high", ["note"] = new { body = "  " } });
        Assert.True(bad);
        Assert.Contains("note", text);
        Assert.Equal("low", WorkItemsApi.Get(107)!.Priority);   // refused before anything was changed
    }

    [Fact(DisplayName = "board_update testsAdd, testsRemove, docsAdd, docsRemove and commitsAdd change the lists without re-sending them, and tests together with testsAdd is refused")]
    public async Task AddAndRemoveListEntries()
    {
        repo.Write("results/run.trx", ReadingLinkedTestRunsTests.Trx);
        ReadingLinkedTestRunsTests.UseResults(repo, "results/*.trx");
        await Ok("board_update", new()
        {
            ["id"] = 107, ["tests"] = new[] { new { suite = "UptimeTests", test = "Ping test" } },
            ["docs"] = new[] { "docs/a.md" }, ["commits"] = new[] { "abc1234" },
        });

        await Ok("board_update", new()
        {
            ["id"] = 107, ["testsAdd"] = new[] { new { suite = "UptimeTests", test = "Page load test" } },
            ["docsAdd"] = new[] { "docs/b.md" }, ["commitsAdd"] = new[] { "def5678", "abc1234" },
        });

        WorkItems.PublicDTOs.WorkItem item = WorkItemsApi.Get(107)!;
        Assert.Equal(["Ping test", "Page load test"], item.Tests!.Select(t => t.Test));
        Assert.Equal(["docs/a.md", "docs/b.md"], item.Docs);
        Assert.Equal(["abc1234", "def5678"], item.Commits);

        await Ok("board_update", new()
        {
            ["id"] = 107, ["testsRemove"] = new[] { new { suite = "UptimeTests", test = "Ping test" } }, ["docsRemove"] = new[] { "docs/a.md" },
        });

        item = WorkItemsApi.Get(107)!;
        Assert.Equal(["Page load test"], item.Tests!.Select(t => t.Test));
        Assert.Equal(["docs/b.md"], item.Docs);
        Assert.Equal(["abc1234", "def5678"], item.Commits);

        (bool isError, JsonElement _, string text) = await Call("board_update", new()
        {
            ["id"] = 107, ["tests"] = new[] { new { suite = "UptimeTests", test = "Ping test" } }, ["testsAdd"] = new[] { new { suite = "UptimeTests", test = "Ping test" } },
        });
        Assert.True(isError);
        Assert.Contains("testsAdd", text);
        Assert.Contains("tests", text);
        Assert.Equal(["Page load test"], WorkItemsApi.Get(107)!.Tests!.Select(t => t.Test));
    }

    [Fact(DisplayName = "board_update corrects a test link's suite when the display name is unique in the latest results and says so, and lists the classes when it is ambiguous")]
    public async Task CorrectsTheSuite()
    {
        repo.Write("results/run.trx", StrictTrx(("UptimeTests", "Ping test"), ("ClassA", "Same name"), ("ClassB", "Same name")));
        ReadingLinkedTestRunsTests.UseResults(repo, "results/*.trx");

        JsonElement r = await Ok("board_update", new()
        {
            ["id"] = 107,
            ["tests"] = new[] { new { suite = "WrongTests", test = "Ping test" }, new { suite = "WrongTests", test = "Same name" }, new { suite = "WrongTests", test = "Nowhere" } },
        });

        string[] warnings = Strings(r.GetProperty("warnings"));
        Assert.Contains(warnings, w => w.Contains("suite corrected from WrongTests to UptimeTests") && w.Contains("Ping test"));
        Assert.Contains(warnings, w => w.Contains("Same name") && w.Contains("ClassA") && w.Contains("ClassB"));
        Assert.Contains(warnings, w => w.Contains("Nowhere") && w.Contains("not in the latest results"));
        Assert.Equal(3, warnings.Length);
        Assert.Equal(
            [("UptimeTests", "Ping test"), ("WrongTests", "Same name"), ("WrongTests", "Nowhere")],
            WorkItemsApi.Get(107)!.Tests!.Select(t => (t.Suite, t.Test)));

        JsonElement added = await Ok("board_update", new() { ["id"] = 107, ["testsAdd"] = new[] { new { suite = "Oops", test = "Ping test" } } });
        Assert.Contains(Strings(added.GetProperty("warnings")), w => w.Contains("suite corrected from Oops to UptimeTests"));
        Assert.Single(WorkItemsApi.Get(107)!.Tests!, t => t.Test == "Ping test");   // the corrected link is the one that was there already
    }

    [Fact(DisplayName = "board_update resolution hides the item, writes the decision note and logs it; an unknown resolution is refused")]
    public async Task Resolution()
    {
        JsonElement r = await Ok("board_update", new() { ["id"] = 107, ["resolution"] = "duplicate of #wi123", ["resolutionWhy"] = "Same export, tracked there.", ["agent"] = "Codex" });

        WorkItems.PublicDTOs.WorkItem item = WorkItemsApi.Get(107)!;
        Assert.True(item.Hidden);
        Assert.Equal("Planned", item.Status);
        Notes.PublicDTOs.Note note = Notes.NotesApi.GetForWorkItem(107).Single(n => n.Title == "Duplicate of #wi123");
        Assert.Equal(("decision", "Codex"), (note.Kind, note.By));
        Assert.Contains("Same export, tracked there.", note.Body);
        WorkItemEvent logged = WorkItemsApi.GetEvents(107).Single(e => e.Kind == "resolution");
        Assert.Equal(("duplicate of #wi123", "Codex"), (logged.Text, logged.By));
        Assert.Contains(Strings(r.GetProperty("applied")), a => a.Contains("resolution"));

        await Ok("board_update", new() { ["id"] = 123, ["resolution"] = "cancelled" });
        Assert.Contains(Notes.NotesApi.GetForWorkItem(123), n => n.Title == "Cancelled" && n.Kind == "decision");
        Assert.True(WorkItemsApi.Get(123)!.Hidden);
        Assert.Equal("In Progress", WorkItemsApi.Get(123)!.Status);

        await Ok("board_update", new() { ["id"] = 107, ["resolution"] = "superseded by #wi123" });
        Assert.Contains(Notes.NotesApi.GetForWorkItem(107), n => n.Title == "Superseded by #wi123");

        (bool isError, JsonElement _, string text) = await Call("board_update", new() { ["id"] = 107, ["resolution"] = "dropped" });
        Assert.True(isError);
        Assert.Contains("cancelled", text);
        Assert.Contains("duplicate of #wiN", text);
        Assert.Contains("superseded by #wiN", text);
        (bool noTarget, JsonElement _, string missing) = await Call("board_update", new() { ["id"] = 107, ["resolution"] = "duplicate of #wi99999" });
        Assert.True(noTarget);
        Assert.Contains("99999", missing);
    }

    [Fact(DisplayName = "board_create takes a folder name, refuses one that exists, and cuts the slug of a long title to a few words")]
    public async Task CreateFolderAndShortSlug()
    {
        OpenSpecStoreApi.Open(repo.Root);

        JsonElement named = await Ok("board_create", new() { ["title"] = "Rate-limit the API", ["why"] = Why, ["whatChanges"] = "- A limit.", ["folder"] = "api-rate-limit" });
        Assert.Equal("api-rate-limit", named.GetProperty("folder").GetString());
        Assert.True(Directory.Exists(repo.PathOf("openspec/changes/api-rate-limit")));

        (bool exists, JsonElement _, string text) = await Call("board_create", new() { ["title"] = "Another", ["why"] = Why, ["whatChanges"] = "- A limit.", ["folder"] = "api-rate-limit" });
        Assert.True(exists);
        Assert.Contains("api-rate-limit", text);
        Assert.Contains("exists", text);
        (bool badName, JsonElement _, string bad) = await Call("board_create", new() { ["title"] = "Another", ["why"] = Why, ["whatChanges"] = "- A limit.", ["folder"] = "Not Kebab/Case" });
        Assert.True(badName);
        Assert.Contains("kebab-case", bad);

        JsonElement longTitle = await Ok("board_create", new() { ["title"] = "Rate limit every public API endpoint per key so that abusive clients cannot starve the others", ["why"] = Why, ["whatChanges"] = "- A limit." });
        Assert.Equal("rate-limit-every-public-api-endpoint", longTitle.GetProperty("folder").GetString());

        JsonElement longWords = await Ok("board_create", new() { ["title"] = "Internationalisation Documentation Infrastructure Modernisation", ["why"] = Why, ["whatChanges"] = "- A limit." });
        Assert.Equal("internationalisation-documentation", longWords.GetProperty("folder").GetString());   // a third word would pass 48 characters: cut at the word before

        (bool ok, string message, int id) = WorkItemsApi.Create("Make the weekly digest email a lot shorter and move the long text behind a link for everyone");
        Assert.True(ok, message);
        Assert.Equal("make-the-weekly-digest-email-a", WorkItemsApi.Get(id)!.Folder);   // every creator, the app included, gets the short slug
    }

    [Fact(DisplayName = "board_log appends one log event without a note file and returns the revision; a long or multi-line text is refused")]
    public async Task Log()
    {
        int notes = Notes.NotesApi.GetForWorkItem(107).Count;
        string before = (await Ok("board_get", new() { ["id"] = 107 })).GetProperty("revision").GetString()!;

        JsonElement r = await Ok("board_log", new() { ["id"] = 107, ["text"] = "ran the suite: 525 passed", ["agent"] = "Codex" });

        Assert.Equal(107, r.GetProperty("id").GetInt32());
        JsonElement e = r.GetProperty("event");
        Assert.Equal(("log", "Codex", "ran the suite: 525 passed"), (e.GetProperty("kind").GetString(), e.GetProperty("by").GetString(), e.GetProperty("text").GetString()));
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d$", e.GetProperty("at").GetString());
        WorkItemEvent last = WorkItemsApi.GetEvents(107)[^1];
        Assert.Equal(("log", "ran the suite: 525 passed", "Codex"), (last.Kind, last.Text, last.By));
        Assert.Equal(notes, Notes.NotesApi.GetForWorkItem(107).Count);
        JsonElement got = await Ok("board_get", new() { ["id"] = 107 });
        Assert.NotEqual(before, got.GetProperty("revision").GetString());
        Assert.Equal(got.GetProperty("revision").GetString(), r.GetProperty("revision").GetString());
        JsonElement newest = got.GetProperty("worklog")[0];
        Assert.Equal(("log", "ran the suite: 525 passed"), (newest.GetProperty("kind").GetString(), newest.GetProperty("text").GetString()));

        (bool longError, JsonElement _, string longText) = await Call("board_log", new() { ["id"] = 107, ["text"] = new string('x', 301) });
        Assert.True(longError);
        Assert.Contains("board_note", longText);
        (bool multiError, JsonElement _, string multiText) = await Call("board_log", new() { ["id"] = 107, ["text"] = "one\ntwo" });
        Assert.True(multiError);
        Assert.Contains("board_note", multiText);
        Assert.True((await Call("board_log", new() { ["id"] = 107, ["text"] = " " })).IsError);
        Assert.Equal(last.Text, WorkItemsApi.GetEvents(107)[^1].Text);
    }
}
