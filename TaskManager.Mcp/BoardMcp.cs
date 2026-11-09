using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TaskManager.Mcp;

/// <summary>
/// The board as seven MCP tools. A host (desktop app, headless server, stdio shim) registers them with
/// <c>services.AddMcpServer().AddBoardTools()</c> and adds its own transport.
/// </summary>
public static class BoardMcp
{
    public const string ServerName = "repo-tasks";

    /// <summary>
    /// Who is calling, when the transport knows: the HTTP host sets it per request from what the stdio shim sends (its working
    /// directory and its agent name), because one server answers many shims and environment variables cannot tell them apart.
    /// </summary>
    public static readonly AsyncLocal<(string? Repo, string? Agent)> Caller = new();

    /// <summary>False in the headless server: it serves every repository, so its own working directory and RTM_REPO are nobody's default.</summary>
    public static bool DefaultToWorkingDirectory { get; set; } = true;

    /// <summary>What the model reads once when it connects. Claude Code truncates server instructions at 4,096 characters.</summary>
    public const string Instructions = """
        Repo Tasks is the board of work items for the repository (openspec/changes/). Use these tools instead of editing openspec/ by hand: they allocate ids, sign your writes and keep the files valid.

        Every tool takes an optional `repo` (any path inside the repository or a worktree; default: the server's working directory). Writes take `agent` (your name; default Claude). Reads take `format`: concise (default) or detailed.

        Rules
        - One work item per deliverable (one result that can be finished and archived on its own). Steps toward it are subtasks. More than about ten subtasks means several items.
        - Look before you create: board_list (text, feature) so you do not duplicate an item. board_features shows the capabilities.
        - Workflow: board_create (or board_update) -> do the work -> board_update (tick subtasks, link tests and commits, set status) -> board_note (a change log: what changed, in plain words) -> board_validate.
        - Pass `revision` from board_get into board_update and board_note when you read first, so you do not overwrite someone else's edit.
        - Status: Backlog, Planned, Confirmed, In Progress, Blocked, In Review. When the work is done set In Review. Never Completed: the user archives from the app and reviews what you did.
        - Write #wiN for another work item and #nN for a note in any text. Commits that mention #wiN link themselves; others go in `commits`.
        - Linked tests: `tests` take the test class short name as `suite` and the display name as `test`, exactly as the latest results file names them; names that are not in it come back as warnings, so run the tests first.
        - A change that alters what the product does needs a delta spec per capability (`specs`): requirements with SHALL or MUST and a WHEN/THEN scenario. A refactoring or chore needs none.
        - Plans are markdown files you write yourself under the plans folder (DevResources/plans by default); the tools only link them with `plan`.
        - Errors say how to fix the call: read them, fix, call again.
        """;

    /// <summary>Registers the seven board tools and sets the server's name and instructions.</summary>
    public static IMcpServerBuilder AddBoardTools(this IMcpServerBuilder builder)
    {
        builder.Services.Configure<McpServerOptions>(Configure);
        return builder.WithTools<BoardTools>();
    }

    /// <summary>Sets <c>ServerInfo</c> and <c>ServerInstructions</c>; for hosts that build <see cref="McpServerOptions"/> themselves.</summary>
    public static void Configure(McpServerOptions options)
    {
        options.ServerInfo = new Implementation { Name = ServerName, Version = typeof(BoardMcp).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" };
        options.ServerInstructions = Instructions;
    }
}
