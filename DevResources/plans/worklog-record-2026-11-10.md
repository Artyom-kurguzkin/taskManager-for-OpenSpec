# Worklog as the record of work, 10 Nov 2026: events for every write, decisions, incremental entries, MCP-first guide

- **Status:** done 2026-11-10 — integrated into #wi142; 521 fast + 15 CLI tests green
- **Date:** 2026-11-10
- **Author:** Claude (agent)
- **Work item:** #wi142

## Why

The user's words: "From now on, use the mcp to interact with the app. Subagents and supervisor should incrementally add log entries for tasks in the works as they go. DECISIONS should be emphasised: they should be clear and brief. Also, whenever attachment or subtask or whatever is added -> event in the logs. I should be able to easily hide such if needed when reading log entries." And: edit the agent guide and its generator accordingly.

## What the industry does

- Decisions are recorded as short, single-decision records (ADRs): a title, the decision, why, and consequences; kept where the work is, superseded rather than deleted ([Nygard's ADR, via Fowler](https://www.martinfowler.com/bliki/ArchitectureDecisionRecord.html), [adr.github.io](https://adr.github.io), [lightweight ADR template](https://dianayukari.com/templates/lightweight-adrs-for-engineering-leaders)). Ours is lighter still: a note of kind `decision`, two lines, emphasised in the feed.
- Audit logs record every write with actor and time and let the reader filter by kind ([SaaS activity feed and audit log patterns](https://www.saasui.design/blog/saas-activity-feed-audit-log-ux-patterns)); the worklog already has the actor and time, it now gets the missing kinds and an "Edits" filter.

## Decisions

- **Every write logs an event**, same wire format in `events.log` (`time | who | kind | text`). New kinds: `title`, `priority`, `label`, `dates`, `feature` (text `old -> new`), `contributor-added` / `contributor-removed` (the name), `plan-linked` / `plan-unlinked` (the path), `description-edited` (the file), `attachment-added` (`n<note>: <file name>`), and `subtask-added` for every subtask board_create writes.
- **Decision = note kind.** Frontmatter `kind: decision`; `NotesApi.Create(id, by, created, kind)` and `SetKind`; the title defaults to "Decision". The worklog shows it with a DECISION label and an accent mark, no frame. The guide says: what was decided and why, two lines.
- **Filters** in the Worklog block: Notes, Decisions, Events (created, status), Links (commit / test / doc / plan), Subtasks (subtask-*, dependency-*), Edits (title, priority, label, dates, feature, contributor-*, description-edited, attachment-added). Unticking Edits hides the housekeeping.
- **Adding a decision**: an "Add decision" action beside "Add note" in the Worklog block and in the day menu; `board_note` takes `kind: "decision"`.
- **Guide v15**: when the tools are present they are the only way to touch the board; supervisor and subagents write one short entry per event as the work goes; decisions as decision notes; hand rules only as the fallback.
- **MCP bug** found on the way: `board_list` omits `nextAfter` on the last page while its output schema requires it, so the client rejects the result. Fixed in chunk C.

## Chunks

| Chunk | What | Owner | Test |
|---|---|---|---|
| seam | Note.Kind in the Notes module (done in the main session) | Claude | KeepingNotesTests "A note can be a decision…" |
| A | Events for every write | sonnet, worktree | EditingWorkItemsTests, KeepingNotesTests, UsingTheBoardOverMcpTests |
| B | Decisions emphasised; Decisions and Edits filters; Add decision | sonnet, worktree, port 9361 | UsingTheWorklogTests, UsingTheBoardTests; shots light/dark |
| C | Guide v15 + REPO-TASKS.md + server instructions; board_note kind; board_list schema | sonnet, worktree | GuidingAgentsTests, UsingTheBoardOverMcpTests |

## Verification

- Full fast suite green after integration (baseline 506 with the seam).
- Hidden desktop, light and dark: a decision in the feed, the Edits filter hiding a field change, Add decision.
- #wi142 kept current through the tools as the work goes, then In Review.
