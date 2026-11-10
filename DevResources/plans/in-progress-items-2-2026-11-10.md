# In Progress items, round 2, 10 Nov 2026: changed-picker bug, MCP findings, Analytics view

- **Status:** done 2026-11-10 — #wi141 and #wi15 In Review; #wi34 fixed, stays In Progress for its git subtasks; 543 fast + 15 CLI tests green
- **Date:** 2026-11-10
- **Author:** Claude (agent)
- **Work items:** #wi34, #wi141, #wi15

## Why

"work on the current in progress items". Read through the tools, worklogs included:

- #wi34: the user's note n147: "picker is here, but it doesnt do anything - everything is still colored as it used to".
- #wi141: moved to In Progress by the user; 18 findings from a day of driving the board over MCP.
- #wi15: moved to In Progress; "add new view: Analytics; should be similar to github projects - lets me customise my charts to query info".

## What the industry does

- GitHub Projects Insights: a chart is configured with a **Layout** (bar, column, line, stacked), an **X-axis** field, an optional **Group by** field and a **Y-axis** (count of items, or sum/average of a number field); charts are saved and listed; **historical** charts put time on the X-axis and group by status ([GitHub Docs: configuring charts](https://docs.github.com/en/issues/planning-and-tracking-with-projects/viewing-insights-from-your-project/configuring-charts), [Microsoft Learn: insight and automation with projects](https://learn.microsoft.com/en-us/training/modules/manage-work-github-projects/6-insight-automation-with-projects)).
- GitLab Insights does the same from a config file ([GitLab Docs: insights](https://docs.gitlab.com/user/project/insights)); ours is configured in the UI and stored with the board's options.

## Decisions

- **#wi34 is a bug chunk**: reproduce with the user's real view options before touching code; fix where every view's colour comes from.
- **Analytics is a fourth view** beside Board, List and Calendar. A chart = layout (column, bar, line, stacked column) + X-axis (status, feature, label, priority, contributor, or time by week) + group by (same fields, or none) + Y (count of work items, or of subtasks). Charts are saved per board in the board's options, with two defaults: "Items by status" and "Status over time".
- **Over-time charts come from the worklog** (`events.log` status events and creation), so no new storage.
- **Charts are inline SVG** in the existing colour tokens; no chart library. Ceiling: no zoom, no tooltips beyond a title; a library when that is asked for.
- **The board's filters apply to Analytics** (contributor, period, show hidden), so there is one place to filter.
- **MCP**: partial reads on `board_get` (`sections`, `since`, `limit`), concise cut down; writes get the hand-off in one call (inline `note`, `testsAdd` / `docsAdd`, revision back from `board_note`), `resolution` for cancelling, `folder` on create, `board_log` for a one-line event, and a reader for main specs. Dropped as obsolete: findings 1.1, 1.4, 1.9, 1.11, 1.12 (no shim-started server any more, tools load natively), 1.6 and 1.17 (fixed in #wi142).

## Chunks

| Chunk | What | Owner | Test |
|---|---|---|---|
| B | #wi34 bug: changed picker has no effect | sonnet, worktree, port 9381 | regression test; CDP shots light/dark |
| M1 | #wi141 reads | sonnet, worktree | UsingTheBoardOverMcpTests |
| M2 | #wi141 writes | sonnet, worktree | UsingTheBoardOverMcpTests |
| N | #wi15 Analytics view | sonnet, worktree, port 9382 | AnalyticsModel tests, page tests; shots light/dark |

## Verification

- Full fast suite green after integration (baseline 525).
- Hidden desktop, light and dark: changed picker greys untouched items in the user's configuration; Analytics view with both default charts and one custom chart.
- Items kept current through the tools as the work goes; In Review when done.
