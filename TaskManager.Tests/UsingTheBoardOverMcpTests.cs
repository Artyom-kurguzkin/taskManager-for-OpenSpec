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

namespace TaskManager.Tests;

/// <summary>The seven board tools, called the way an agent calls them: through an MCP client over an in-memory pipe.</summary>
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

    [Fact(DisplayName = "The server lists the seven board tools, each with a description")]
    public async Task ListsTools()
    {
        IList<McpClientTool> tools = await client.ListToolsAsync();

        Assert.Equal(
            ["board_create", "board_features", "board_get", "board_list", "board_note", "board_update", "board_validate"],
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

    [Fact(DisplayName = "A tool call reads what is on disk now, without waiting for the file watcher")]
    public async Task FreshReads()
    {
        const string yaml = "openspec/changes/add-health-check/.openspec.yaml";
        Assert.Equal("In Progress", (await Ok("board_get", new() { ["id"] = 123 })).GetProperty("status").GetString());   // the server has the item cached now

        repo.Write(yaml, repo.Read(yaml).Replace("status: In Progress", "status: Blocked"));   // the app, in another process

        Assert.Equal("Blocked", (await Ok("board_get", new() { ["id"] = 123 })).GetProperty("status").GetString());
    }
}
