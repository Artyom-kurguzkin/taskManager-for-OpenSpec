# Plans folder, plan links and the delegation workflow

- **Status:** done, in review
- **Date:** 2026-11-05
- **Author:** Claude (agent)
- **Work item:** #wi89

## Why

The user set a standing workflow for sizeable tasks: research first, write a plan, split it into testable chunks, delegate the chunks to cheaper models in separate worktrees, verify and merge, keep OpenSpec current, leave the result uncommitted for review. The app has to carry two things for this: a place for plans that the user can move, and a way to link a plan from a work item.

## What the industry does

- Design records (ADRs, RFCs) live in one folder in the repo, one file per decision, named by number or date plus a slug, with a status line (proposed / accepted / superseded) and a short fixed structure. Sources: [sentenz ADR convention](https://sentenz.github.io/convention/articles/architecture-decision-records), [EdgeX design docs](https://docs.edgexfoundry.org/3.0/design/adr).
- Agent workflows in 2026: plan with the strongest model, execute with cheaper ones per task; isolate parallel agents in git worktrees because they overwrite each other's files otherwise; keep architectural decisions in the main session; verify before the agent is allowed to finish. Sources: [Rootly](https://rootly.com/blog/a-council-a-sword-and-a-fleet-of-agents-how-i-ship-code-now), [Augment model routing](https://augmentcode.com/guides/ai-model-routing-guide), [DEV: parallel agents](https://dev.to/jovan_chan_9500711396d4e6/parallel-ai-coding-agents-in-2026-how-to-orchestrate-multiple-claude-code-and-cursor-agents-3291), [Claude Code workflows](https://capitalandcompute.net/blog/claude-code-agentic-workflows/).

## Decisions

- Plans are markdown files named `<title-slug>-<yyyy-MM-dd>.md` in a plans folder. Default `DevResources/plans/`.
- The folder is a **per-user, per-repository setting** (`settings.json`, not the repo), so one person can keep plans under a gitignored `local/`. The app writes the current folder into `openspec/REPO-TASKS.md`, so agents that only read the repo still know where plans go; when the setting changes the guide is rewritten.
- A work item links its plan with `x-rtm.plan: <repo-relative path>`. The item page shows it as a chip that opens the file **inside the app**, rendered as markdown (`/doc/<path>`); any markdown file under the repo root can be opened that way.
- The guide gets the workflow as its first section so other agents follow it.

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | `x-rtm.plan` in the WorkItems module: `Rtm.Plan`, `WorkItem.Plan`, `WorkItemsApi.SetPlan(id, path?)` | sonnet, worktree | `EditingWorkItemsTests`: set, read back, clear |
| B | `/doc/{*path}` page rendering a markdown file under the repo root; refuses `..` and missing files | sonnet, worktree | `UsingThePagesTests`: renders, not-found cases |
| C | Plans folder setting per repository (default `DevResources/plans`), Settings page row, guide v4 carries the folder and the workflow, rewritten when the folder changes | main session | `RememberingSettingsTests`, `SwitchingProjectsTests` |
| D | Item page: plan chip (`x-rtm.plan`) opening `/doc/…`; integration of A–C | main session | `UsingThePagesTests` |

A and B are independent; D depends on A and B; C is independent.

## Verification

- Fast suite green; new tests red before their chunk.
- Hidden-desktop run: Settings shows the folder, guide rewritten on change, item page chip opens the rendered plan, light and dark.
- OpenSpec change `plans-and-delegation` validated `--strict`; CLAUDE.md and the guide updated; nothing committed.
