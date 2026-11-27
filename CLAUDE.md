# Working rules for this repository

These apply to every change, every session.

## ALWAYS a diagram when explaining

**Every explanation to the owner comes with a diagram, in the same reply: in chat and in every plan. Always. Never skipped to answer faster, never "offered for later".** How something works, why something happened, a design, options, a before/after: draw it, then say in a line or two how to read it. Only a one-line answer to a quick factual question goes without.

- Write the drawing as `<name>.excalidraw` (the Excalidraw MCP server's element format; `label` on shapes is fine) in the plans folder, run `node DevResources/tools/excalidraw-to-svg.mjs <file>`, look at the render (headless Edge screenshot) before showing it.
- In a plan: embed `![what it shows](<name>.excalidraw.svg)` under the section it explains, before the plan is handed over. One drawing per concept.
- In chat: link the `.excalidraw.svg` in the reply.
- Subagents that write a plan, a research summary or a final report explaining a design or a cause do the same; say so in their brief.
- If the route does not work, say so at the top of the reply. Do not silently skip.

## Every behaviour change

1. **Tests first.** Write the failing test before the implementation. Fast suite: `dotnet test TaskManager.Tests --filter "Category!=OpenSpecCli"`.
2. **Spec with the change.** An OpenSpec change folder under `openspec/changes/<name>/` (`.openspec.yaml` with `x-rtm` id/title/status, `proposal.md`, `tasks.md`, `specs/<capability>/spec.md`) in the same step as the code, validated with `npx -y @fission-ai/openspec@1.14.3 validate <name> --strict`. Compute the next `x-rtm.id` from the max over every `.openspec.yaml` at write time; the user creates and merges items concurrently.
3. **Verify UI in the real app**, one feature at a time, over the Chrome DevTools Protocol (not synthetic OS input), in light and dark; screenshots are the evidence. Tests alone are not enough.
   Run the app on a **hidden desktop** with `tests/hidden-app.ps1` (`New-DemoRepo`, `Start-HiddenApp`, `Eval`/`Click`/`Drag`/`Shot`, `Stop-HiddenApp`): never on the user's desktop, where the window would cover their work and take their focus, and never against the real `openspec/`. Stop it before building; it holds the Debug exe.

## A task that is more than a quick fix

Follow the `delegate-workflow` skill (`~/.claude/skills/delegate-workflow/SKILL.md`): say the chunks first, then the steps below. Bug reports are tasks too; never debug serially in the main tree.

1. **Research** how the industry solves it; keep the sources.
2. **Plan** it in the repository's plans folder (Settings → Plans folder; `DevResources/plans` by default, or whatever `openspec/REPO-TASKS.md` names) as `<title-slug>-<yyyy-MM-dd>.md`, and link it from the work item with `x-rtm.plan`.
3. **Split** it into chunks that are each testable on their own (a unit test and a black-box check): subtasks, or dependent work items.
4. **Delegate** the chunks to cheaper models (Agent tool, `model: sonnet` or `haiku`), each in its own worktree so they never touch the same files and each can build and run its own classes. A worktree starts from HEAD; the staged work of earlier jobs reaches it as a patch (step 7).
5. **The agents' words go to the feed.** Every agent posts its own text to its work item's feed with `board_log` (`id`, `text` up to 4,000 characters over several lines, `kind` from the event standard `started | found | decision | tests-red | tests-green | verified | blocked | done`, `repo` = the main repository path, `agent` = "Claude chunk X"; it answers one line, and longer text becomes a note by itself): each status update it would otherwise only say and its final report, verbatim. Tool calls and edits are not posted: git shows them. The supervisor reads `board_get` with `since` the last look instead of polling worktrees, and adds its own notes at merge (what was integrated, the regression line, the hand-off). Completion notifications can be lost; the feed is the record.
6. When they return, **verify** each chunk and apply its diff against HEAD to the working tree (per file, never a no-index diff against the main tree). **Tests are split by role:** a chunk agent **never** runs the full suite; its brief names the exact test classes it must pass (its own new class plus the existing classes its files touch), and it reports those Passed lines. The full suite is **regression**, run **once after integration** by a separate merge subagent (`model: fable`) that you spawn so the chat is not blocked: it merges the worktrees, builds, runs `dotnet test TaskManager.Tests --filter "Category!=OpenSpecCli"`, and reports the Passed/Failed line. Five agents each running the full suite in parallel overloads the machine and hits the 6-minute abort.
7. Keep the board current as you go (below). **Branches and commits:** each subagent works on its own branch in its own worktree and commits there as it likes. The supervisor never commits and never touches the user's branch history: it merges each agent's branch into the **staging area of the branch the user is on** (git's index) by applying the branch's own commits as one staged change (`git diff <agent base>..<agent tip> | git apply --3way --index`; a real `git merge` refuses a dirty index). The agent's branch starts at HEAD plus one first commit holding the user's current staged work (`git diff --cached --binary HEAD` applied and committed by the agent), so its diff from that base is only its own work. After the merge, the agent's worktree and branch are removed. The user alone commits.

## The board: through the tools, as you go

When the `repo-tasks` MCP tools are loaded (`board_list`, `board_get`, `board_features`, `board_create`, `board_update`, `board_note`, `board_validate`; `.mcp.json` at the repo root starts them), they are the **only** way to touch the board: every read and write (items, notes, subtasks, test links, spec deltas via `board_update specs`, status), never a hand edit of `openspec/`. They allocate the ids under a lock, stamp `agentBy`/`agentAt`, check test names and validate. Pass the `revision` from `board_get` into writes, set `In Review` when done, never `Completed`. The hand rules below are only for when the tools cannot be loaded (say so when that happens).

**Log as you go.** While a task is in the works, the supervisor and every subagent write one short `board_note` per event on the work item: chunk started, tests red, a blocker, a command and its result (code block), chunk finished (the Passed line), integrated. Two sentences each, not one summary at the end. Subagents in worktrees pass `repo` = the main repository path and `agent: "Claude (chunk X)"`.

**Decisions** are decision notes (`board_note` with `kind: "decision"`): clear and brief, what was decided and why, two lines. The app emphasises them in the worklog.

**Read the whole item, not just the description.** When asked to look through the In Progress items (or any set of items), `board_get` each one in `detailed` format and read its worklog: notes, decisions and events added since you last looked are part of the brief, and often change it. Say what is new before acting.

## After the work: update the board

When a piece of work lands, find the work items it touches — the change you made **and** any older item whose topic it advances — in every status except Completed (archived), and update each:

- **Change-log note**: `openspec/changes/<change>/notes/n<id>.md`, id = max over every `notes/n*.md` in the repo + 1; frontmatter `id`, `by: Claude`, `created: yyyy-MM-ddTHH:mm:ss`; then `# <title>` and what changed, in plain words.
- **Linked tests**: `x-rtm.tests: [{suite: <test class short name>, test: "<display name>"}]` on one line in the change's `.openspec.yaml`. Names must match `TaskManager.Tests/TestResults/latest.trx`; refresh it with `dotnet test TaskManager.Tests --filter "Category!=OpenSpecCli" --logger "trx;LogFileName=latest.trx"`.
- **Linked commits**: `x-rtm.commits: [<7-char hash>]` for commits that don't mention `#wiN` (those link themselves). Never commit unasked; say which commits could be linked once the user has committed.
- **Spec and tasks text** of those items, where the work changed the behaviour they describe.

## Mark your work as an agent's

The app flags what an agent added or changed until the user reviews it. Whenever you create or edit a work item, note or subtask, stamp it (full conventions in `openspec/REPO-TASKS.md`):

- Work item: `agentBy: Claude` and `agentAt: <yyyy-MM-ddTHH:mm:ss>` in the change's `x-rtm` block (bump `agentAt` on every later edit; never write `reviewedAt` — the app does).
- Note: the same two keys in the note's frontmatter.
- Subtask: end the line in `tasks.md` with `<!-- agent: Claude -->`.
