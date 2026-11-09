# Working rules for this repository

These apply to every change, every session.

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
4. **Delegate** the chunks to cheaper models (Agent tool, `model: sonnet` or `haiku`), each with `isolation: worktree` so they never touch the same files. A worktree starts from HEAD: chunks that depend on uncommitted work stay in the main session.
5. **Poll** the agents every ~5 minutes (`git -C .claude/worktrees/<agent> status --short`); ping one silent for 10 minutes; completion notifications can be lost. Tell the user the poll result each time.
6. When they return, **verify** each chunk, apply its diff against HEAD to the working tree (per file, never a no-index diff against the main tree), and run the whole suite for integration.
7. Keep the board current as you go (below) and leave everything **uncommitted** in the working tree for review.

## After the work: update the board

When the `repo-tasks` MCP tools are loaded (`board_list`, `board_get`, `board_features`, `board_create`, `board_update`, `board_note`, `board_validate`; `.mcp.json` at the repo root starts them), use them for every board write (create, update, note, validate) instead of hand-editing `openspec/`: they allocate the ids under a lock, stamp `agentBy`/`agentAt`, check test names and validate. Pass the `revision` from `board_get` into writes, set `In Review` when done, never `Completed`. The hand rules below stay as the fallback when the tools cannot be loaded.

When a piece of work lands, find the work items it touches — the change you made **and** any older item whose topic it advances — in every status except Completed (archived), and update each:

- **Change-log note**: `openspec/changes/<change>/notes/n<id>.md`, id = max over every `notes/n*.md` in the repo + 1; frontmatter `id`, `by: Claude`, `created: yyyy-MM-ddTHH:mm:ss`; then `# <title>` and what changed, in plain words.
- **Linked tests**: `x-rtm.tests: [{suite: <test class short name>, test: "<display name>"}]` on one line in the change's `.openspec.yaml`. Names must match `TaskManager.Tests/TestResults/latest.trx`; refresh it with `dotnet test TaskManager.Tests --filter "Category!=OpenSpecCli" --logger "trx;LogFileName=latest.trx"`.
- **Linked commits**: `x-rtm.commits: [<7-char hash>]` for commits that don't mention `#wiN` (those link themselves). Never commit unasked; say which commits could be linked.
- **Spec and tasks text** of those items, where the work changed the behaviour they describe.

## Mark your work as an agent's

The app flags what an agent added or changed until the user reviews it. Whenever you create or edit a work item, note or subtask, stamp it (full conventions in `openspec/REPO-TASKS.md`):

- Work item: `agentBy: Claude` and `agentAt: <yyyy-MM-ddTHH:mm:ss>` in the change's `x-rtm` block (bump `agentAt` on every later edit; never write `reviewedAt` — the app does).
- Note: the same two keys in the note's frontmatter.
- Subtask: end the line in `tasks.md` with `<!-- agent: Claude -->`.
