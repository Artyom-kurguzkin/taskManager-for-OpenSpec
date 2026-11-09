# One MCP server for every agent — 9 Nov 2026

- **Status:** planned, not started
- **Date:** 2026-11-09
- **Author:** Claude (agent)
- **Work item:** #wi120
- **Replaces:** the hand-edited conventions in `openspec/REPO-TASKS.md` §1–§5 as the primary way agents touch the board (the file stays as the fallback)

## Why

Agents edit `openspec/` by hand today, following a 200-line guide: compute the next `x-rtm.id` from the max over every yaml, stamp `agentBy`/`agentAt`, number notes from the repo-wide max, match test names to the trx, validate with the pinned CLI. Every one of those is a convention an agent can get wrong. The "yet another collision" commit and `ResolveDuplicateIds` are evidence that they do. The 5 Nov "no collisions" plan chose *repair after the fact*; this plan moves to *prevention at one allocator*.

The app already has the right implementation of all of it in the module facades (`WorkItemsApi`, `NotesApi`, `FeaturesApi`, `GitHistoryApi`, `LinkedTestRunsApi`): static methods, primitive parameters, `(Success, Message)` returns. An MCP server is a thin wrapper over them, so the conventions move from prose the agent reads into code it cannot skip.

Requirements from the user:
1. Cover as much ground as possible (everything an agent does to the board goes through tools).
2. Many agents → **one centralised server**; the collision problem is solved there.
3. Must not rely on the desktop app running: the app gets an **MCP/API-only mode**.

## What the industry does (sources)

**Protocol.** The current MCP spec is **2026-07-28**; it removed sessions (`Mcp-Session-Id`) and the `initialize` handshake, so Streamable HTTP is stateless by default and "one independent process serving many clients" is the shape the spec itself describes ([changelog](https://modelcontextprotocol.io/specification/2026-07-28/changelog), [Streamable HTTP](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http)). Local servers SHOULD bind to 127.0.0.1, MUST validate `Origin`, and for local HTTP the spec's own advice is a token or restricted IPC rather than OAuth ([security best practices](https://modelcontextprotocol.io/specification/2026-07-28/basic/security_best_practices)). stdio servers take credentials from the environment and must keep stdout clean ([stdio](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio)). Tool errors the model can fix go back as `isError: true`, not protocol errors; structured results carry `structuredContent` plus the same JSON as text ([tools](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)).

**SDK.** The official C# SDK is stable: `ModelContextProtocol` 2.4.0, `.AspNetCore` 2.4.0, net8–net10; HTTP is stateless by default; tools are `[McpServerToolType]` classes with `[McpServerTool(ReadOnly, Destructive, Idempotent, UseStructuredContent)]`; DI into tool constructors; `McpException` for messages the client should see; in-memory `StreamServerTransport`/`StreamClientTransport` over a `Pipe` for tests ([NuGet](https://www.nuget.org/packages/ModelContextProtocol), [getting started](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/getting-started.html), [stateless](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html), [transports](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/transports/transports.html), [v2 announcement](https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/)). `AddConsole` logs to stdout: set `LogToStandardErrorThreshold = Trace` for stdio.

**Tool design.** Few high-level tools beat many CRUD tools; namespace by service; `response_format` concise/detailed saved about a third of the tokens; paginate; errors must say how to fix the call ([Writing effective tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents)). Accuracy drops as tool count grows; deferral pays off past ~10 tools ([Advanced tool use](https://anthropic.com/engineering/advanced-tool-use), [OSWorld-MCP](https://arxiv.org/pdf/2510.24563)). Claude Code loads tool names first and descriptions on demand, truncates server instructions at 4,096 chars, caps tool output at 25,000 tokens ([Claude Code MCP](https://code.claude.com/docs/en/mcp)). Trackers in the wild: Linear merged create+update into one `save_issue`; Atlassian shows 6 "primary" tools of 52 and defers the rest; GitHub groups toolsets and has `--read-only`; Beads takes `workspace_root` on every call and names the actor with an env var ([Linear](https://linear.app/docs/mcp), [Atlassian](https://support.atlassian.com/atlassian-rovo-mcp-server/docs/supported-tools/), [GitHub MCP](https://github.com/github/github-mcp-server), [Beads MCP](https://github.com/steveyegge/beads/blob/main/integrations/beads-mcp/README.md)). No official `openspec mcp` exists: PR #488 was closed unmerged ([cli.md](https://github.com/Fission-AI/OpenSpec/blob/main/docs/cli.md), [PR 488](https://github.com/Fission-AI/OpenSpec/pull/488)).

**Ids and concurrency.** Sequential ids collide across branches and agents; Beads moved to hash ids for that reason, Backlog.md kept `TASK-N` and ships a `doctor` to repair duplicates after merges ([Beads hash ids](https://beads.gascity.com/core-concepts/hash-ids), [Backlog.md 1.48](https://newreleases.io/project/github/MrLesk/Backlog.md/release/v1.48.0)). Git worktrees share the object db, refs and config in the common dir (`git rev-parse --git-common-dir`); files placed there are shared by every worktree and never merged ([git-worktree](https://git-scm.com/docs/git-worktree)). `FileShare.None` is a mandatory kernel lock on Windows, released when the process dies; on Unix it is advisory `flock` ([FileShare](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare), [source](https://source.dot.net/System.Private.CoreLib/R/f1a729e977e368e7.html)). Named mutexes are thread-affine and leave `AbandonedMutexException` behind a crash. Compare-and-swap on a content hash is how community MCP editors avoid lost updates ([mcp-text-editor](https://mcp.so/servers/mcp-text-editor), [RFC 9110 If-Match](https://www.rfc-editor.org/rfc/rfc9110.html#name-if-match)). Lazy daemons (Bazel, Gradle, Nx, Beads) are discovered by a well-known file, version-checked, replaced on mismatch, and exit after ~3 h idle ([Bazel](https://bazel.build/run/client-server), [Gradle](https://docs.gradle.org/current/userguide/gradle_daemon.html), [Nx](https://nx.dev/docs/concepts/nx-daemon)).

## Decisions

1. **Three layers, each correct without the one above it.**
   - *Layer 1, allocator.* Ids (work items and notes) are allocated under a cross-process lock in the git common dir, scanning every worktree. The desktop app and the server both use it. Correctness never depends on a daemon.
   - *Layer 2, tools.* One tool library over the module facades, usable in-process by any host.
   - *Layer 3, hosts.* The same exe runs as the desktop app, as a headless server (`--serve`), or as a stdio shim (`--mcp`) that finds or starts the server and forwards to it.
2. **One server per machine, not per repo.** Streamable HTTP, stateless, `127.0.0.1`, bearer token from a per-user token file, `Origin` validated. Every tool takes `repo` (any path inside a repo or worktree; the server resolves the root). The shim fills `repo` from its own working directory, so agents in worktrees never have to pass it.
3. **stdio shim in `.mcp.json`, not an HTTP entry.** stdio is what every client and every worktree subagent supports; an HTTP entry would fail whenever nothing is listening. The shim reads `%LOCALAPPDATA%/RepoTaskManager/server.json` (pid, port, version), starts `TaskManager.exe --serve` detached when nothing answers, asks an older-version server to shut down first (Bazel pattern), and if the server still cannot be reached serves the tools in-process over the same allocator. Users who prefer a direct HTTP entry can still add one.
4. **The desktop app hosts the server too** when the port is free, so "app running" and "server running" are the same thing; when a headless server already holds the port the app keeps writing files directly, which the allocator and the store's stale-write check keep safe. The headless server exits after 3 h idle unless started with `--idle 0`.
5. **Ids stay `#wiN`.** Readability wins; the user links `#wi123` everywhere. Next id = max(counter in common dir, max `x-rtm.id` over every worktree's `openspec/changes/**`) + 1, taken under the lock. Same for notes with `n<N>`. Remote-branch duplicates remain possible and keep going through `ResolveDuplicateIds` (the existing repair), never through a remote fetch (Backlog.md's regression).
6. **Edits are compare-and-swap.** `board_get` returns a `revision` (hash of the item's files); `board_update` and `board_note` accept it and refuse with the current revision when it moved. Optional so a quick status change does not need a read first, recommended in the tool description.
7. **Seven tools, no deferral.** Fewer than ten, so Claude Code loads them all; names are prefixed `board_` and Claude Code adds the server prefix. Archive and delete are not tools: `Completed` is refused with the message "the user archives from the app", as the workflow already says.
8. **Agent identity** is a parameter with a default: `agent` on every write, defaulting to `RTM_AGENT` from `.mcp.json` (`Claude`). Every write stamps `agentBy`/`agentAt`, note `by`, subtask `<!-- agent: X -->`, and `contributors` on create, exactly as the guide demands today.
9. **Server instructions** (under 4,096 chars) carry the short rules: one item per deliverable, `#wiN` links, revision use, validate before stopping. `REPO-TASKS.md` gains a §0 line "if the `repo-tasks` tools are present, use them; the rest of this file is what they do for you".
10. **Not doing:** REST beside MCP (MCP *is* the API), resources/prompts (model-controlled calls belong in tools), DuckDB or any index (the in-memory cache is the index), hash ids, remote fetch on allocation, Native AOT.

## Tool surface

Every tool: `repo` (optional, resolved as above), `format: concise|detailed` on reads (default concise), markdown text + `structuredContent`.

| Tool | Does | Annotations |
|------|------|-------------|
| `board_list` | Items filtered by `status[]`, `feature`, `label`, `contributor`, `text`, `ready` (unblocked, not In Progress), `includeArchived`; cursor `after`/`limit` (default 50). Concise: id, title, status, feature, label, revision. | read-only |
| `board_get` | One item: header, proposal, tasks with numbers and done flags, notes (title, by, created; bodies in detailed), delta specs (detailed), linked tests with ✓/✗ against the latest trx, linked commits plus commits mentioning `#wiN`, dependencies and what it blocks, `revision`. | read-only |
| `board_features` | Capabilities under `openspec/specs/` and `x-rtm.features` in `config.yaml`, with the open items on each. | read-only |
| `board_create` | `title`, `why`, `whatChanges`, `feature`, `label`, `status` (Backlog/Planned/In Progress), `priority`, `plan`, `dependsOn[]`, `subtasks[]` (grouped text), `specs{capability: markdown}`, `agent`. Allocates the id under the lock, writes a valid folder (`skip_specs` synced), stamps, runs `validate --strict`, returns id, folder, validation output. | destructive=false, idempotent=false |
| `board_update` | `id`, optional `revision`, and any of: `title`, `status`, `priority`, `label`, `feature`, `started`, `due`, `plan`, `dependsOn`, `contributors`, `description` (proposal body), `tests[]` (checked against the latest trx; unknown names reported), `commits[]`, `hidden`, `subtasks[]` (`{number?, text?, done?}`: new ones appended to their group, signed), `specs{capability: markdown|null}`. Status `In Progress` adds the agent to contributors and stamps `started`; blocked-by-dependency and archived rules come from `WorkItemsApi.SetStatus`. Runs `validate --strict` when files other than yaml changed. | destructive=false, idempotent=true |
| `board_note` | `id`, `title`, `body`, `links[]`, `commits[]`, `attachments[]` (URLs), `agent`, optional `revision`. Allocates the note id under the lock, stamps `by`/`agentBy`/`agentAt`. | destructive=false |
| `board_validate` | `id` or all open items: pinned OpenSpec CLI `validate --strict`, plus the app's own warnings (duplicate ids, Completed-but-not-archived, test names not in the trx). | read-only |

Everything agents do by hand today maps onto these: create item (§3), sign (§1), change log, tests, commits, plan, subtasks, status/dates (§2), validate (§4), numbers (§5). What stays manual: writing the plan file (it is a markdown file in the plans folder, the tool only links it) and archiving (the user, from the app).

## Hosts and processes

```
TaskManager.exe                    desktop app; also hosts /mcp on 127.0.0.1:<port> when the port is free
TaskManager.exe --serve [--port N] [--idle H]   headless: Kestrel loopback, stateless MCP, /health, /shutdown (token), server.json, idle exit
TaskManager.exe --mcp [--agent X]  stdio shim: ensure server (start / replace older) → forward; else serve in-process
```

- Token: `%LOCALAPPDATA%/RepoTaskManager/token` (created once, random, 0600-equivalent); clients send `Authorization: Bearer`. `server.json`: `{pid, port, version, started}`.
- Default port 7431; `--port 0` picks a free one and records it. `Origin` present and not `http://127.0.0.1:*`/`http://localhost:*` → 403.
- Logs: file under `%LOCALAPPDATA%/RepoTaskManager/logs/`, never stdout; the shim logs to stderr.
- Multi-repo inside one process: `OpenSpecStoreApi` has one *current* repo, so the server serialises tool calls with one `SemaphoreSlim(1,1)` and switches with `Open(repo, keepOthers: true)`/`Use(repo)` per call. `// ponytail: one global lock; per-repo locks if agents ever queue behind each other`.
- WinExe and stdio: a GUI-subsystem exe inherits redirected std handles when a parent spawns it with pipes, which is how Claude Code starts stdio servers; chunk D verifies this first thing and falls back to a 20-line console exe `rtm-mcp` if it does not hold.
- `.mcp.json` in this repo: `{"mcpServers":{"repo-tasks":{"type":"stdio","command":"${RTM_APP:-C:/Users/artyo/Documents/5-mine/repo-task-manager/dist/TaskManager.exe}","args":["--mcp"],"env":{"RTM_AGENT":"Claude"}}}}`. The Settings page shows the same snippet with the running exe's path for other repositories.

## Allocator (layer 1)

- `OpenSpecStoreApi.Allocate(string counter, Func<IEnumerable<string>, int> maxOver)` → next id. Lock: exclusive `FileStream` on `<git-common-dir>/rtm/<counter>.lock` (`FileShare.None`, retry with backoff for 5 s, pid+start time written inside for diagnosis); no git → `%TEMP%/rtm/<hash of root>/`. Siblings from `git worktree list --porcelain -z`; `maxOver` receives every worktree root and returns the max id it finds. Counter file `<common>/rtm/<counter>` keeps the last handed-out number, so a deleted or hidden item never frees its number.
- Callers: `WorkItems.Commands.CreateFolder`, `MoveSubtasksToNew/Dependency`, `Merge`, `ResolveDuplicateIds` (work-item counter); `Notes.Commands.Create` (note counter). `StampProvisionalIds` stays.
- Writes stay as they are (`WriteText` with the store's stale check); atomic temp+`File.Move(overwrite)` is added inside `WriteText` only, one place.

## Chunks

Phase 1 runs in parallel from HEAD; phase 2 starts after phase 1 is integrated, because the tools code against the allocator and revision APIs.

| # | Chunk | Owns | Must not touch | Unit test | Black-box check |
|---|-------|------|----------------|-----------|-----------------|
| A | Allocator in the common dir, worktree-wide max, atomic write | `OpenSpecStore/*` (new `IdAllocator.cs`, `WriteText`), `WorkItems/Commands.cs` id call sites, `Notes/Commands.cs` create | tools, hosts | `EditingRepoFilesSafelyTests`: two parallel creates in two worktrees of one repo get different ids; counter survives a deleted item; lock released after a killed holder | create an item in the app and one via `openspec`-less script in a sibling worktree; ids differ |
| B | Revision on items and compare-and-swap | `WorkItems/Queries.cs` (`Revision` in the DTO), `WorkItemsApi.Update*(…, expectedRevision)`, `NotesApi.Create(…, expectedRevision)` | A's files except a one-line call | `EditingWorkItemsTests`: stale revision refused with the current one; matching revision accepted | — (exercised by E) |
| C | Tool library `TaskManager.Mcp` with the seven tools against *today's* APIs (no revision yet, ids via existing `NextId`), structured DTOs, agent stamping, repo resolution, global semaphore, server instructions | new project `TaskManager.Mcp/`, `RepoTaskManager.slnx` | modules, `TaskManager/` | new `UsingTheBoardOverMcpTests` (in-memory pipe client): list/get/features/create/update/note/validate round-trips on the sample repo; stamps present; `Completed` refused | `claude -p` against the stdio shim once D lands |
| D | Hosts: `--serve`, `--mcp` shim (discover, start, version check, forward, in-process fallback), token, Origin, server.json, idle exit, `/health`, `/shutdown`, logging to file/stderr | `TaskManager/Program.cs` (branch before Photino), new `TaskManager/Hosting/*`, `TaskManager.csproj` refs | tool bodies (uses C's project with an `echo`-level placeholder until integration) | new `ServingTheBoardTests` (Category `Server`): `--serve` answers `/health`; wrong token 401; foreign `Origin` 403; shim starts a server when none, reuses it when present, replaces an older `version` | WinExe stdio inheritance spike first; then `claude mcp list` sees the server |
| G | Guide and docs: `AgentGuide` v12 (§0 "use the tools when present"), `CLAUDE.md`, `README.md`, `.mcp.json`, Settings page snippet | `TaskManager/Services/AgentGuide.cs`, `CLAUDE.md`, `README.md`, `.mcp.json`, `Components/Pages/Settings*.razor` | everything else | `GuidingAgentsTests`: v12 text present, older copy refreshed | Settings page shows the snippet (CDP, light and dark) |
| E | Wire C to A and B: real allocator, `revision` in/out, test-name check against the trx, validate after writes | `TaskManager.Mcp/*` | hosts | extend `UsingTheBoardOverMcpTests`: concurrent creates over two clients never collide; stale revision refused | — |
| F | Desktop app hosts `/mcp` when the port is free; server status chip in Settings | `TaskManager/Program.cs`, `Workspace.cs`, Settings page | modules | `ServingTheBoardTests`: app-mode host answers `/health`; yields when a `--serve` holds the port | hidden app + `--mcp` shim: create an item over MCP, the board shows it with the agent stamp, light and dark screenshots |

Owners: A, B, C, D, G → sonnet agents in worktrees (phase 1); E, F → sonnet agents in worktrees after phase-1 integration; integration, the WinExe spike decision and the final verification → main session. Note: the main tree has large uncommitted edits (`BoardPage`, `Commands.cs`, specs); phase-1 agents start from HEAD, so A's `Commands.cs` hunks are applied per file with `git apply --include`, never a whole-tree reset.

## Verification

- Red first in every chunk; fast suite green after each integration: `dotnet test TaskManager.Tests --filter "Category!=OpenSpecCli&Category!=Server"`; the `Server` category runs once per integration (it starts processes).
- Collision proof: two shims in two worktrees each create an item at the same moment, 20 times; `board_validate` reports no duplicate ids.
- Lifecycle proof: kill `--serve`; the next shim call restarts it; bump the version string; the next shim call replaces it.
- Real app, hidden desktop, light and dark: item created over MCP appears with the agent stamp; Settings shows server status and the snippet.
- Nothing committed; board updated (#wi120 note, tests, status).

## Risks

- **WinExe stdio** (decision 3): verified first in D; fallback console exe.
- **SDK 2.x API drift** from blog samples: agents read the v2 docs linked above, not tutorials.
- **Claude Code and worktree subagents**: the docs only state `.mcp.json` resolution for desktop-app worktree sessions; D tests an `isolation: worktree` subagent calling `board_list`.
- **Static store, one current repo**: the global semaphore is the ceiling; fine for a handful of agents, revisit if calls queue.
