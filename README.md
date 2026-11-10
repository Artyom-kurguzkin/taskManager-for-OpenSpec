# Repo Tasks

A desktop task board for a git repository that uses [OpenSpec](https://github.com/Fission-AI/OpenSpec). It has no database: work items, subtasks, features and notes are the files in the repo's `openspec/` folder. Every change you make in the app is a normal file edit you can review and commit with git.

- **Board and list views:** group by status, feature or contributor. Drag cards between columns, add sub-categories, merge items, and show subtasks as cards.
- **Work items:** each OpenSpec change is a work item. Edit the title, description (`proposal.md`), status, priority, features, contributors and subtasks (`tasks.md`). Moving an item to Completed archives the change with the OpenSpec CLI.
- **Notes:** markdown notes stored next to their work item. `[[doc]]`, `#wi123` and `#n123` links resolve as you type.
- **Git history:** each item's change log, plus commits that mention `#wi123`.

Built with .NET 10 and Blazor, in a [Photino](https://www.tryphotino.io/) window.

## 1. Install the prerequisites

The commands below are for **Windows 10/11**, using `winget`, which ships with Windows. Open a new terminal after installing, so `PATH` picks up the new tools.

| Tool | Needed for | Install |
|---|---|---|
| .NET 10 SDK | building and running the app | `winget install Microsoft.DotNet.SDK.10` |
| Microsoft Edge WebView2 Runtime | the app window (already on Windows 11) | `winget install Microsoft.EdgeWebView2Runtime` |
| Git | change log and linked commits | `winget install Git.Git` |
| Node.js LTS | moving items to Completed (`openspec archive`), creating `openspec/` in a repo, and the OpenSpec CLI test | `winget install OpenJS.NodeJS.LTS` |

The app still runs without Git or Node.js. Without Git, history panels say "Not a git repository". Without Node.js, archiving to Completed fails with a message saying the OpenSpec CLI couldn't be run.

Check everything is on `PATH`:

```powershell
dotnet --version   # 10.0.x
git --version
node --version
npx --version
```

## 2. Get the code and build it

```powershell
git clone <this repo's URL> repo-task-manager
cd repo-task-manager
dotnet build RepoTaskManager.slnx
```

The first build downloads the NuGet packages (Photino.Blazor, Radzen.Blazor for the list view's data grid, YamlDotNet, Markdig, ModelContextProtocol for the agent tools, xunit, bUnit).

Run the tests:

```powershell
# everything except the slow OpenSpec CLI check (no Node.js needed)
dotnet test TaskManager.Tests --filter Category!=OpenSpecCli

# everything, including `openspec validate --strict` on a repo the app has edited (needs Node.js, ~10 s)
dotnet test TaskManager.Tests

# only the MCP server and stdio entry, run as real processes (~15 s, ports 7450-7459, their own RTM_HOME)
dotnet test TaskManager.Tests --filter Category=Server
```

## 3. Have a repository with an `openspec/` folder

The app opens a repo that already contains `openspec/`. This repo has one, so you can try the app on itself.

To set up OpenSpec in another repo:

```powershell
cd path\to\your-repo
npx -y @fission-ai/openspec@1.14.3 init
```

## 4. Run the app

From the project root, pass the repo to open:

```powershell
dotnet run --project TaskManager -- "C:\path\to\your-repo"
```

Without a path, the app reopens the last repo you used, or else the current folder. You can switch repos any time in **Settings → Repository** (Browse… or paste a path, then Open).

If you make your own Start Menu or desktop shortcut for quicker access, point it at `TaskManager\bin\Release\net10.0\TaskManager.exe` and build with `dotnet build TaskManager -c Release` (the default `dotnet build`/`dotnet run` produce a separate `bin\Debug\` copy). A shortcut left pointing at a `Debug` build, or at an old `dist\` exe from step 5, will keep launching whatever was there last, not your latest change.

## 5. Build a standalone exe (optional)

```powershell
dotnet publish TaskManager -p:PublishProfile=dist
```

- This creates `dist\TaskManager.exe` with a `dist\wwwroot\` folder next to it. Keep them together.
- The exe is self-contained and compressed (about 37 MB, because it includes the .NET runtime), so the target PC doesn't need .NET installed. It still needs WebView2, and Git or Node.js for the features above.
- Run it with `dist\TaskManager.exe [repo path]`.
- Both profiles target Windows x64 only.

For a much smaller exe (about 3.5 MB) that uses the .NET runtime installed on the PC:

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10   # once, on the target PC
dotnet publish TaskManager -p:PublishProfile=dist-small
```

This adds `dist\TaskManager-small.exe`, which shares the same `dist\wwwroot\`.

## Agents over MCP

The app can serve the board to coding agents (Claude Code and other [MCP](https://modelcontextprotocol.io/) clients) as eight tools: `board_list`, `board_get`, `board_features`, `board_create`, `board_update`, `board_note`, `board_log` and `board_validate`. They sit on the same code the app uses, so ids are allocated under a lock, what an agent writes is signed with its name, and test names and OpenSpec validity are checked, instead of an agent following the conventions in `openspec/REPO-TASKS.md` by hand (that file stays as the fallback and is rewritten with the instructions for the tools). With the tools loaded they are the only way to touch the board, and the supervisor and every subagent log as they go: one short `board_note` per event while the work is in progress (chunk started, tests red, a blocker, a command and its result, chunk finished), and each decision as a decision note (`kind: "decision"`: what was decided, why, what it rules out), which the worklog emphasises.

| Tool | What it does |
|---|---|
| `board_list` | work items filtered by status, feature, label, contributor, title text or `ready` (can be started now), in pages |
| `board_get` | one item in full: description, subtasks, notes, the worklog (notes and events, newest first), delta specs, linked tests with their outcome, commits, documents, dependencies, and its `revision` |
| `board_features` | the features with the open items on each |
| `board_create` | a new work item with the next id: proposal, subtasks, delta specs, plan and dependencies, signed and validated; the change folder is `folder` or the first words of the title (at most 6 words and 48 characters) |
| `board_update` | status, priority, label, feature, dates, plan, dependencies, contributors, description, tests, commits, documents, subtasks and specs of an item; `testsAdd`, `testsRemove`, `docsAdd`, `docsRemove` and `commitsAdd` change a list without re-sending it; `note` writes a worklog note in the same call; `resolution` (`cancelled`, `duplicate of #wiN`, `superseded by #wiN`) hides the item and writes the decision note |
| `board_note` | a worklog entry on an item (a note, with the next note id) and the item's new `revision`; the title is optional |
| `board_log` | one line in an item's worklog (`kind: log`, at most 300 characters, no note file), for events like "ran the suite: 525 passed"; returns the new `revision` |
| `board_validate` | `openspec validate --strict` for one item or every open one, plus the app's own warnings |

What the tools guarantee:

- **No two items share a number.** Work-item and note ids come from one counter under a file lock in `.git/rtm/`, shared by every git worktree of the repository and by the desktop app. A deleted item's number is not handed out again.
- **No lost edits.** `board_get` returns a `revision`; `board_update` and `board_note` refuse a write that carries an older one and say the current one. A write without a revision goes through unchecked.
- **Agents never archive.** Status `Completed` is refused: set `In Review` and archive from the app.
- **The worklog keeps itself.** What a tool call changes (status, links, subtasks, dependencies) is recorded in the item's worklog under the agent's name; the agent writes a `board_note` only for what the tools cannot see, such as a command it ran and what came back.

One server serves every repository on the machine. The same exe runs in three ways:

| Command | What it does |
|---|---|
| `TaskManager.exe` | the desktop app. It starts the server when it opens (a newer build replaces an older server); **Settings → Agents over MCP** shows whether it is running, on which port, and starts or stops it |
| `TaskManager.exe --serve [--port N] [--idle H] [--parent PID]` | headless server for streamable HTTP MCP at `http://127.0.0.1:7431/mcp` (loopback only). The app starts it with `--parent <its pid>`, and it exits when that process ends. Without `--parent` it exits after `H` idle hours (three by default, `--idle 0` = never) |
| `TaskManager.exe --mcp [--agent X]` | stdio entry for MCP clients: finds the running server and forwards to it. It never starts one: while the app is closed a tool call gets an error asking you to open it, and the next call works once you have |

The server runs while the app is open: open the app before using the tools, and closing it stops the server. The two are separate processes on purpose: the app shows what an agent wrote within a moment (it watches the files), and an agent reads what you changed in the app on its next call.

Most clients want the stdio entry. Add this to `.mcp.json` in the repository the agents work in (the Settings page shows it with this machine's path; `claude mcp add repo-tasks -- <path to TaskManager.exe> --mcp` does the same):

```json
{
  "mcpServers": {
    "repo-tasks": {
      "type": "stdio",
      "command": "C:/path/to/TaskManager.exe",
      "args": ["--mcp"],
      "env": { "RTM_AGENT": "Claude" }
    }
  }
}
```

`RTM_AGENT` is the name the tools sign with. The path is specific to the machine; a `.mcp.json` that is committed can use `"${RTM_APP:-C:/path/to/TaskManager.exe}"` so each person can point `RTM_APP` at their own exe.

The stdio entry tells the server which repository it was started in and which agent it speaks for, so an agent never has to pass `repo`: a call acts on the repository the client runs in, also from a git worktree.

A client that only speaks HTTP connects to `--serve` with the header `Authorization: Bearer <token>`. The token is the content of `%LOCALAPPDATA%\RepoTaskManager\token`, created on first use and readable by you only. Requests without it get 401, and requests from a browser origin other than localhost get 403. Over plain HTTP every call must pass `repo` (any path inside the repository): the server serves all of them and has no default.

| Environment variable | What it does |
|---|---|
| `RTM_AGENT` | the name the tools sign with (default `Claude`); `--agent X` and a tool's `agent` argument override it |
| `RTM_PORT` | the port the app's server listens on (default 7431) |
| `RTM_HOME` | where the token, `server.json` and logs live instead of `%LOCALAPPDATA%\RepoTaskManager`, for demos and tests |
| `RTM_APP` | not read by the app: the variable a committed `.mcp.json` can use for the exe's path (above) |

When something goes wrong, the server's log is `%LOCALAPPDATA%\RepoTaskManager\logs\serve-<date>.log`; the stdio entry logs to stderr, which the client shows. After `dotnet publish` or a rebuild, the app replaces a running server of an older build when it starts.

## Where things are stored

| Data | Location | In git? |
|---|---|---|
| Work items, subtasks, specs | standard OpenSpec files in `openspec/` | yes |
| Item metadata (number, title, status, priority, contributors, lanes, hidden, linked commits, tests and documents) | `x-rtm:` block in each change's `.openspec.yaml` | yes |
| Board sub-categories, new feature sub-groups | `x-rtm:` block in `openspec/config.yaml` | yes |
| Notes | `openspec/changes/<change>/notes/n<id>.md` (they move with the change when it's archived) | yes |
| Events of a work item (created, status changes, links, ticked subtasks, dependencies: who and when) | `openspec/changes/<change>/events.log`, one line each; the item page's Worklog shows them with the notes; when a rule such as `*.log` would make git ignore them, opening the repository adds `!openspec/**/events.log` to its `.gitignore` | yes |
| Your settings (theme, last repo, name, board view per repo) | `%APPDATA%\RepoTaskManager\settings.json` | no |
| The last work-item and note numbers handed out, and their lock files | `.git/rtm/` in the repository (shared by its worktrees); `%TEMP%\rtm\` for a folder without git | no |
| The MCP server's token, `server.json` (pid, port, version) and logs | `%LOCALAPPDATA%\RepoTaskManager\` | no |

Set the `RTM_SETTINGS` environment variable to a file path to use a different settings file, for example for demos.

## Project layout

A modular monolith with one static class library per domain entity, plus the app host:

- `OpenSpecStore/`: repo folder, safe file reads and writes, the `x-rtm` YAML block, the file watcher, the id allocator
- `GitHistory/`: git CLI wrapper
- `Features/`, `WorkItems/`, `Notes/`: the domain modules
- `LinkedTestRuns/`: reads the latest test results file for linked tests
- `TaskManager.Mcp/`: the eight `board_*` MCP tools over the modules (no UI, no host)
- `TaskManager/`: the Photino + Blazor app, the only project with UI; `Hosting/` holds the headless server (`--serve`) and the stdio entry (`--mcp`)
- `TaskManager.Tests/`: xunit and bUnit tests, plus a sample repo in `Fixtures/`
- `openspec/`: this project's own specs and changes
- `design/mockup.html`: the design system mockup

## Notes

Deleting a work item doesn't remove it from OpenSpec. It only hides it from the app (`x-rtm.hidden: true`) until the workflow matures. Tick "show hidden" under **view** on the board and use **Restore** on the item's page to bring it back.

Only Windows has been tested so far. Photino also supports macOS and Linux (Linux needs WebKitGTK), but the app hasn't been run there.

## Acknowledgements

The design system was inspired by Pharrell Tan's work: https://dribbble.com/shots/16333822-Personal-Task-Management-App. Please keep this acknowledgement if you clone this project, unless you make your own theme.
