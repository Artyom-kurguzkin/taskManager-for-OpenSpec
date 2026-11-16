using System.IO.Pipelines;
using System.Text.Json;
using Features;
using GitHistory;
using LinkedTestRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenSpecStore;
using TaskManager.AppState;
using TaskManager.Mcp;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// One process hosts the app's windows and the agents' tool calls, so a call (an agent's, the watcher's timer, a sync pass) works in its own repository and under its own name
/// without moving the one the app shows. The interleavings are driven by hand: a call stops at a gate inside its repository and the app acts meanwhile, so no test waits for a clock.
/// </summary>
public sealed class KeepingCallsOnTheirOwnRepositoryTests : IAsyncLifetime
{
    private readonly TestRepo mine = new(), theirs = new();
    private ServiceProvider services = null!;
    private McpServer server = null!;
    private McpClient client = null!;

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
        mine.Dispose();
        theirs.Dispose();
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task<JsonElement> Tool(string name, string repo, Dictionary<string, object?> args)
    {
        args["repo"] = repo;
        CallToolResult result = await client.CallToolAsync(name, args);
        Assert.True(result.IsError != true, string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        return result.StructuredContent is { } s ? s.Clone() : default;
    }

    [Fact(DisplayName = "An agent writing to another repository while the board loads leaves the board on its own repository")]
    public async Task AgentInAnotherRepositoryLeavesTheBoardOnItsOwn()
    {
        OpenSpecStoreApi.Open(mine.Root);
        OpenSpecStoreApi.Open(theirs.Root, keepOthers: true);
        OpenSpecStoreApi.Use(mine.Root);
        GitHistoryApi.Configure(mine.Root);
        string statusBefore = WorkItemsApi.Get(123)!.Status;
        string mineYaml = mine.Read("openspec/changes/add-health-check/.openspec.yaml");
        const string Events = "openspec/changes/add-health-check/events.log";
        string mineEvents = File.Exists(mine.PathOf(Events)) ? mine.Read(Events) : "";
        TaskCompletionSource inside = Gate(), go = Gate(), wrote = Gate(), finish = Gate();

        Task<(string? Root, string Status)> agent = Task.Run(async () =>
        {
            using IDisposable repository = OpenSpecStoreApi.InRepo(theirs.Root), history = GitHistoryApi.InRepo(theirs.Root);
            inside.SetResult();
            await go.Task;
            Assert.True(WorkItemsApi.SetStatus(123, "In Review").Success);   // written in the agent's repository
            wrote.SetResult();
            await finish.Task;
            return (OpenSpecStoreApi.RepoRoot, WorkItemsApi.Get(123)!.Status);
        });

        try
        {
            await inside.Task;
            Assert.Equal(mine.Root, OpenSpecStoreApi.RepoRoot);   // the agent is inside its repository; the board still reads mine
            Assert.Equal(statusBefore, WorkItemsApi.Get(123)!.Status);
            go.SetResult();
            await wrote.Task;
            Assert.Equal(mine.Root, OpenSpecStoreApi.RepoRoot);   // after the agent's write: the board still loads mine, unchanged
            Assert.Equal(statusBefore, WorkItemsApi.Get(123)!.Status);
            Assert.Equal(mineYaml, mine.Read("openspec/changes/add-health-check/.openspec.yaml"));
            Assert.Equal(mineEvents, File.Exists(mine.PathOf(Events)) ? mine.Read(Events) : "");
        }
        finally { finish.TrySetResult(); }

        (string? agentRoot, string agentStatus) = await agent;
        Assert.Equal(theirs.Root, agentRoot);
        Assert.Equal("In Review", agentStatus);
        Assert.Contains("In Review", theirs.Read("openspec/changes/add-health-check/.openspec.yaml"));
        Assert.Equal(mine.Root, OpenSpecStoreApi.RepoRoot);
        Assert.Equal(statusBefore, WorkItemsApi.Get(123)!.Status);
    }

    [Fact(DisplayName = "A tab read in passing is read in its own scope: the active project, and what git history says of it, stay mine")]
    public async Task ReadingAnotherTabKeepsMyHistory()
    {
        Directory.CreateDirectory(mine.PathOf(".git")); mine.Write(".git/HEAD", "1111111111111111111111111111111111111111\n");
        Directory.CreateDirectory(theirs.PathOf(".git")); theirs.Write(".git/HEAD", "2222222222222222222222222222222222222222\n");
        OpenSpecStoreApi.Open(mine.Root);
        OpenSpecStoreApi.Open(theirs.Root, keepOthers: true);
        OpenSpecStoreApi.Use(mine.Root);
        GitHistoryApi.Configure(theirs.Root);
        GitHistoryApi.Configure(mine.Root);
        TaskCompletionSource inside = Gate(), finish = Gate();

        Task<string> other = Task.Run(async () =>
        {
            using IDisposable repository = OpenSpecStoreApi.InRepo(theirs.Root), history = GitHistoryApi.InRepo(theirs.Root);
            inside.SetResult();
            await finish.Task;
            return GitHistoryApi.HeadToken();
        });

        try
        {
            await inside.Task;
            Assert.Equal("1111111111111111111111111111111111111111", GitHistoryApi.HeadToken());
            Assert.Equal(mine.Root, OpenSpecStoreApi.RepoRoot);
        }
        finally { finish.TrySetResult(); }

        Assert.Equal("2222222222222222222222222222222222222222", await other);
        Assert.Equal("1111111111111111111111111111111111111111", GitHistoryApi.HeadToken());
    }

    [Fact(DisplayName = "Edits made in the app while an agent works are signed by me, not the agent")]
    public async Task AppEditsWhileAnAgentWorksAreSignedByMe()
    {
        OpenSpecStoreApi.Open(mine.Root);
        WorkItemsApi.ConfigureActor(() => "Me");
        TaskCompletionSource inside = Gate(), go = Gate(), logged = Gate();

        Task agent = Task.Run(async () =>
        {
            using IDisposable actor = WorkItemsApi.ActingAs("Claude");
            inside.SetResult();
            await go.Task;
            Assert.True(WorkItemsApi.LogEvent(123, "custom", "agent step").Success);
            logged.SetResult();
        });

        await inside.Task;
        Assert.True(WorkItemsApi.LogEvent(123, "custom", "app step before").Success);   // the agent is mid-call: this is the app's own edit
        go.SetResult();
        await logged.Task;
        await agent;
        Assert.True(WorkItemsApi.LogEvent(123, "custom", "app step after").Success);   // and the agent's call did not take my name with it

        Assert.Equal([("Me", "app step before"), ("Claude", "agent step"), ("Me", "app step after")],
            WorkItemsApi.GetEvents(123).Where(e => e.Kind == "custom").Select(e => (e.By, e.Text)));
    }

    [Fact(DisplayName = "An agent's call does not change who the app signs as, in its own repository or any other")]
    public async Task AgentCallLeavesTheAppsSignature()
    {
        OpenSpecStoreApi.Open(mine.Root);
        WorkItemsApi.ConfigureActor(() => "Me");

        await Tool("board_note", theirs.Root, new() { ["id"] = 123, ["body"] = "Looked at it.", ["agent"] = "Claude" });
        Assert.True(WorkItemsApi.SetStatus(123, "In Review").Success);

        Assert.Equal("Me", WorkItemsApi.GetEvents(123).Last(e => e.Kind == "status").By);
    }

    [Fact(DisplayName = "An agent's call does not add its repository to my open projects")]
    public async Task AgentCallDoesNotOpenItsRepository()
    {
        OpenSpecStoreApi.Open(mine.Root);
        theirs.Write("openspec/changes/export-remittances/.openspec.yaml", "schema: spec-driven\nx-rtm:\n  id: 107\n  title: Second repo item\n");

        JsonElement got = await Tool("board_get", theirs.Root, new() { ["id"] = 107 });
        await Tool("board_note", theirs.Root, new() { ["id"] = 107, ["body"] = "Wired.", ["agent"] = "Claude" });

        Assert.Equal("Second repo item", got.GetProperty("title").GetString());   // the call did read and write the other repository
        Assert.Equal([mine.Root], OpenSpecStoreApi.OpenRoots);
        Assert.Equal(mine.Root, OpenSpecStoreApi.RepoRoot);
        Assert.Equal("Monthly remittance export", WorkItemsApi.Get(107)!.Title);
        Assert.Contains("Wired.", Directory.GetFiles(theirs.PathOf("openspec/changes/export-remittances/notes")).Select(File.ReadAllText).Single(t => t.Contains("Wired.")));
    }

    [Fact(DisplayName = "An agent's write to a repository the app has open is seen by the app's board")]
    public async Task AgentWriteInAnOpenRepositoryIsSeen()
    {
        OpenSpecStoreApi.Open(mine.Root);
        OpenSpecStoreApi.Open(theirs.Root, keepOthers: true);
        OpenSpecStoreApi.Use(mine.Root);
        Assert.Equal("Monthly remittance export", WorkItemsApi.Get(107)!.Title);

        await Tool("board_update", theirs.Root, new() { ["id"] = 107, ["title"] = "Renamed by the agent", ["agent"] = "Claude" });

        Assert.Equal([mine.Root, theirs.Root], OpenSpecStoreApi.OpenRoots);
        Assert.Equal(mine.Root, OpenSpecStoreApi.RepoRoot);
        Assert.Equal("Monthly remittance export", WorkItemsApi.Get(107)!.Title);
        using (OpenSpecStoreApi.InRepo(theirs.Root)) Assert.Equal("Renamed by the agent", WorkItemsApi.Get(107)!.Title);
    }

    [Fact(DisplayName = "A reopened repository shows its new features and test results")]
    public void ReopenedRepositoryShowsItsNewContent()
    {
        FakeTimeProvider clock = new();
        TestResultsCache.Clock = clock;   // never advanced: only the repository session can say the cached scan is old
        mine.Write("openspec/config.yaml", "schema: spec-driven\nx-rtm:\n  testResults:\n    - results/*.trx\n");
        mine.Write("results/run.trx", ReadingLinkedTestRunsTests.Trx);
        OpenSpecStoreApi.Open(mine.Root);
        Assert.DoesNotContain(FeaturesApi.GetAll(), f => f.Path == "billing-rules");
        Assert.Contains(LinkedTestRunsApi.Latest().Results, r => r.Test == "Ping test");

        OpenSpecStoreApi.Close();   // the folder changes while it is closed (a pull, another tool), and opens again with its version back at 0
        mine.Write("openspec/specs/billing-rules/spec.md", "# Billing rules\n");
        mine.Write("results/run.trx", ReadingLinkedTestRunsTests.Trx.Replace("Ping test", "Pong test"));
        OpenSpecStoreApi.Open(mine.Root);

        Assert.Contains(FeaturesApi.GetAll(), f => f.Path == "billing-rules");
        Assert.Contains(LinkedTestRunsApi.Latest().Results, r => r.Test == "Pong test");
    }

    [Fact(DisplayName = "Test results are read again once the reuse window has passed, on the clock the host gives")]
    public void TestResultsFollowTheHostsClock()
    {
        FakeTimeProvider clock = new();
        TestResultsCache.Clock = clock;
        mine.Write("openspec/config.yaml", "schema: spec-driven\nx-rtm:\n  testResults:\n    - results/*.trx\n");
        mine.Write("results/run.trx", ReadingLinkedTestRunsTests.Trx);
        OpenSpecStoreApi.Open(mine.Root);
        Assert.Contains(LinkedTestRunsApi.Latest().Results, r => r.Test == "Ping test");

        mine.Write("results/second.trx", ReadingLinkedTestRunsTests.Trx.Replace("Ping test", "Pong test"));
        File.SetLastWriteTimeUtc(mine.PathOf("results/second.trx"), File.GetLastWriteTimeUtc(mine.PathOf("results/run.trx")).AddMinutes(1));
        Assert.Contains(LinkedTestRunsApi.Latest().Results, r => r.Test == "Ping test");   // inside the two seconds: the scan is reused
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Contains(LinkedTestRunsApi.Latest().Results, r => r.Test == "Pong test");
    }
}
