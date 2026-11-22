using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TaskManager.Mcp;

/// <summary>
/// What the app that serves the tools tells them. <paramref name="AfterWrite"/>: called after a call that wrote, so the window shows it (in the app's own process the file watcher
/// ignores the app's own writes, and an agent's is one). <paramref name="DefaultToWorkingDirectory"/>: false in the app, which serves every repository, so its own working directory and RTM_REPO are nobody's default.
/// </summary>
public sealed record BoardHost(Action? AfterWrite = null, bool DefaultToWorkingDirectory = true);

/// <summary>
/// The board as eight MCP tools. The app (<c>TaskManager/Api</c>) registers them with
/// <c>services.AddMcpServer().AddBoardTools(host)</c> and adds the HTTP transport; the stdio relay (<c>McpShim</c>) only borrows their definitions.
/// </summary>
public static class BoardMcp
{
    public const string ServerName = "repo-tasks";

    /// <summary>
    /// Who is calling, when the transport knows: the HTTP host sets it per request from what the stdio shim sends (its working
    /// directory and its agent name), because one server answers many shims and environment variables cannot tell them apart.
    /// </summary>
    public static readonly AsyncLocal<(string? Repo, string? Agent)> Caller = new();

    /// <summary>What the model reads once when it connects. Claude Code truncates server instructions at 4,096 characters.</summary>
    public const string Instructions = """
        Repo Tasks is the board of work items (openspec/changes/). These tools are the only way to touch the board: never edit openspec/ by hand while they load. They allocate ids, sign your writes and keep the files valid. Every tool takes `repo` (any path inside the repository or a worktree); writes take `agent` (your name; default RTM_AGENT, else Claude); reads take `format` concise or detailed.

        Rules
        - One work item per deliverable; its steps are subtasks (more than about ten means several items). Look before you create: board_list, board_features.
        - Workflow: board_create or board_update -> do the work -> board_update (tick subtasks, link tests, commits and docs, set the status; its `note` writes the hand-off in the same call) -> board_validate. Set In Review when done, never Completed: the user archives. Statuses: Backlog, Planned, Confirmed, In Progress, Blocked, In Review (a project may add more).
        - Before acting on an item, board_get it in detailed format and read its worklog: notes, decisions and events added since you last looked are part of the brief; say what is new before acting. board_get takes `sections`, `since` and `limit`.
        - board_update's list fields (dependsOn, contributors, tests, commits, docs) replace the whole list: read first, or use testsAdd, testsRemove, docsAdd, docsRemove, commitsAdd. Pass `revision` from board_get when you read first. Subtasks: `number` with `done`, `text` or `remove: true` changes one; without `number` it adds one.
        - A change that alters what the product does needs a delta spec per capability (`specs`: capability path to markdown): requirements with SHALL or MUST, each with a #### Scenario of WHEN and THEN lines. To MODIFY a requirement repeat it whole: board_features with format detailed, `feature` and `requirement` returns its text. A refactoring or chore needs none.
        - An item that ends undone: board_update `resolution` (cancelled, duplicate of #wiN, superseded by #wiN) hides it and writes a decision note.
        - Linked tests: `suite` is the test class short name, `test` the display name, exactly as the latest results file; unknown names come back as warnings, so run the tests first. A plan is a markdown file you write in the plans folder (openspec/REPO-TASKS.md names it) and link with `plan`. Write #wiN or #nN to link items and notes; commits that mention #wiN link themselves.
        - Errors say how to fix the call: read them, fix, call again.

        Log as you go: the supervisor and every subagent post one short event per step with board_log id=N kind=<kind> text=..., which answers one line. Kinds: started, found, decision (line one what was decided, line two why), tests-red, tests-green (with the Passed line), verified, blocked, done (the final report); leave `kind` out for a plain line. The text may span lines up to 4,000 characters (longer becomes a note). A subagent in a worktree passes `repo` = the main repository and its own `agent`. board_note (kind decision for a decision) is for a hand-off worth a note file; status changes, links and ticked subtasks are logged for you.
        """;

    /// <summary>Registers the eight board tools and sets the server's name and instructions. <paramref name="host"/>: what the app that serves them tells them (none: a plain library host).</summary>
    public static IMcpServerBuilder AddBoardTools(this IMcpServerBuilder builder, BoardHost? host = null)
    {
        if (host is not null) builder.Services.AddSingleton(host);
        builder.Services.Configure<McpServerOptions>(Configure);
        return builder.WithTools<BoardTools>(ResultOptions);
    }

    /// <summary>
    /// How tool results are serialised and their output schemas generated. The SDK's defaults drop null properties while the schema
    /// lists every property without a default value as required, so a client that enforces the schema (Claude Code) rejects the result.
    /// Writing nulls makes the JSON agree with the schema for every tool; optional properties (record parameters with a default) are
    /// not in the schema's required list and may still carry JsonIgnore(WhenWritingNull) to keep concise results short.
    /// </summary>
    public static readonly JsonSerializerOptions ResultOptions = new(McpJsonUtilities.DefaultOptions) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    /// <summary>Sets <c>ServerInfo</c> and <c>ServerInstructions</c>; for hosts that build <see cref="McpServerOptions"/> themselves.</summary>
    public static void Configure(McpServerOptions options)
    {
        options.ServerInfo = new Implementation { Name = ServerName, Version = typeof(BoardMcp).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" };
        options.ServerInstructions = Instructions;
    }
}
